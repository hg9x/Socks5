﻿using System;
using System.Collections.Generic;
using System.Text;
using socks5.Plugin;
using System.Net;
using socks5.TCP;
using socks5.Encryption;
namespace socks5.Socks
{
    public class SocksClient
    {
        public event EventHandler<SocksClientEventArgs> onClientDisconnected = delegate { };

        public Client Client;
        public int Authenticated { get; private set; }
        public SocksClient(Client cli)
        {
            Client = cli;
        }
        private SocksRequest req1;
        public SocksRequest Destination { get { return req1; } }
        public void Begin(int PacketSize, int Timeout)
        {
            Client.onClientDisconnected += Client_onClientDisconnected;
            List<AuthTypes> authtypes = Socks5.RequestAuth(this);
            if (authtypes.Count <= 0)
            {
                Client.Send(new byte[] { 0x00, 0xFF });
                Client.Disconnect();
                return;
            }
            this.Authenticated = 0;
            SocksEncryption w = null;
            List<object> lhandlers = PluginLoader.LoadPlugin(typeof(LoginHandler));
            //check out different auth types, none will have no authentication, the rest do.
            if (lhandlers.Count > 0 && authtypes.Contains(AuthTypes.SocksBoth) || authtypes.Contains(AuthTypes.SocksEncrypt) || authtypes.Contains(AuthTypes.SocksCompress) || authtypes.Contains(AuthTypes.Login))
            {
                //this is the preferred method.
                w = Socks5.RequestSpecialMode(authtypes, Client);
                foreach (LoginHandler lh in lhandlers)
                {
                    if (lh.Enabled)
                    {
                        //request login.
                        User user = Socks5.RequestLogin(this);
                        if (user == null)
                        {
                            Client.Disconnect();
                            return;
                        }
                        LoginStatus status = lh.HandleLogin(user);
                        Client.Send(new byte[] { (byte)HeaderTypes.Socks5, (byte)status });
                        if (status == LoginStatus.Denied)
                        {
                            Client.Disconnect();
                            return;
                        }
                        else if (status == LoginStatus.Correct)
                        {
                            Authenticated = (w.GetAuthType() == AuthTypes.Login ? 1 : 2);
                            break;
                        }
                    }
                }
            }
            else if (authtypes.Contains(AuthTypes.None))
            {
                //no authentication.
                if (lhandlers.Count <= 1)
                {
                    //unsupported methods y0
                    Authenticated = 1;
                    Client.Send(new byte[] { (byte)HeaderTypes.Socks5, (byte)HeaderTypes.Zero });
                }
                else
                {
                    //unsupported.
                    Client.Send(new byte[] { (byte)HeaderTypes.Socks5, (byte)AuthTypes.Unsupported });
                    Client.Disconnect();
                    return;
                }
            }
            else
            {
                //unsupported.
                Client.Send(new byte[] { (byte)HeaderTypes.Socks5, (byte)AuthTypes.Unsupported });
                Client.Disconnect();
                return;
            }
            //Request Site Data.
            if (Authenticated == 1)
            {
                w = new SocksEncryption();
                w.SetType(AuthTypes.Login);
                SocksRequest req = Socks5.RequestTunnel(this, w);
                if (req == null) { Client.Disconnect(); return; }
                req1 = new SocksRequest(req.StreamType, req.Type, req.Address, req.Port);
                //call on plugins for connect callbacks.
                foreach (ConnectHandler conn in PluginLoader.LoadPlugin(typeof(ConnectHandler)))
                    if (conn.Enabled)
                        if (conn.OnConnect(req1) == false)
                        {
                            req.Error = SocksError.Failure;
                            Client.Send(req.GetData(true));
                            Client.Disconnect();
                            return;
                        }
                //Send Tunnel Data back.
                SocksTunnel x = new SocksTunnel(this, req, req1, PacketSize, Timeout);
                x.Open();
            }
            else if (Authenticated == 2)
            {
                SocksRequest req = Socks5.RequestTunnel(this, w);
                if (req == null) { Client.Disconnect(); return; }
                req1 = new SocksRequest(req.StreamType, req.Type, req.Address, req.Port);
                foreach (ConnectHandler conn in PluginLoader.LoadPlugin(typeof(ConnectHandler)))
                    if (conn.Enabled)
                        if (conn.OnConnect(req1) == false)
                        {
                            req.Error = SocksError.Failure;
                            Client.Send(req.GetData(true));
                            Client.Disconnect();
                            return;
                        }
                //Send Tunnel Data back.
                SocksSpecialTunnel x = new SocksSpecialTunnel(this, w, req, req1, PacketSize, Timeout);
                x.Open();
            }
        }

        void Client_onClientDisconnected(object sender, ClientEventArgs e)
        {
            this.onClientDisconnected(this, new SocksClientEventArgs(this));
            Client.onClientDisconnected -= Client_onClientDisconnected;
            //added to clear up memory
        }
    }
    public class User
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public IPEndPoint IP { get; private set; }
        public User(string un, string pw, IPEndPoint ip)
        {
            Username = un;
            Password = pw;
            IP = ip;
        }
    }
}
