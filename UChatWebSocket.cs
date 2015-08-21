using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SpeedLogger4UChat
{
    public struct conn_info
    {
        public Dictionary<string, string> cii; //연결시 추가적으로 서버에 보내야 하는 정보들 [예:md5, mb_id ···]
        public string room;
        public string nick;
    }
    public struct user_info
    {
        public DateTime conn;
        public bool admin;
        public bool login;
        public string mb_id;
    }
    enum WebSockState
	{
        HandShaking, Connecting, Connected, Closed
	}

    public class UChatWebSocket
    {
        private TcpClient TC;
        private System.Text.StringBuilder SB;
        private System.Threading.Thread Recv_Thread;
        private WebSockState State;
        private System.Threading.Mutex mtx;

        private conn_info ci;
        public string nick
        {
            get { return ci.nick; }
            set { Send("5:::{\"name\":\"set_nick\",\"args\":[{\"nick\":\"E_Sukmean\"}]}"); ci.nick = value; }
        }
        public string room
        {
            get { return ci.room; }
        }

        public UChatWebSocket()
        {
            TC = new TcpClient();
            mtx = new System.Threading.Mutex();
            SB = new System.Text.StringBuilder(250);
        }

        public void Connect(int port, conn_info ci)
        {
            if (TC.Connected == true) throw new Exception("이미 연결되어 있습니다.");
            if (ci.room == null || ci.room == "") throw new Exception("방 이름이 설정되어 있지 않습니다.");
            if (ci.nick == null || ci.nick == "") throw new Exception("닉네임이 설정되어 있지 않습니다.");
            this.ci = ci;
            TC.Connect("chat.uchat.co.kr", port);

            byte[] buf = System.Text.Encoding.UTF8.GetBytes("GET /socket.io/1/websocket/" + time_token(port.ToString()) + " HTTP/1.1\r\nHost: chat.uchat.co.kr:" + port.ToString() + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nUser-Agent: UChat_Bot\r\nSec-WebSocket-Key: RV9TdWttZWFuJ3MgVUNoYXQgV2ViU29ja2V0TGli\r\nOrigin: https://www.esukmean.com\r\nSec-WebSocket-Version: 13\r\n\r\n");
            TC.GetStream().Write(buf, 0, buf.Length);

            State = WebSockState.Connecting;
            Recv_Thread = new System.Threading.Thread(monitor);
            Recv_Thread.Start();
        }
        public void Close()
        {
            if (TC.Connected == false) throw new Exception("연결되어 있지 않습니다.");

            TC.Close();
        }

        public void Send(string msg)
        {
            if (TC.Connected == false) throw new Exception("소켓이 연결되지 않았습니다.");

            byte[] msg_byte = System.Text.Encoding.UTF8.GetBytes(msg);
            byte[] to_send = new byte[msg_byte.Length + 100];
            
            int last = 0;
            to_send[last++] = 129;
            
            if (msg_byte.Length < 126)
            {
                to_send[last++]=(byte)msg_byte.Length;
            }
            else if (msg_byte.Length < 65533)
            {
                to_send[last++] = 126;
                byte[] len = BitConverter.GetBytes((short)(msg_byte.Length));
                
                Array.Reverse(len);
                Array.Copy(len, 0, to_send, ++last, 2);
                ++last;
            }
            else {
                to_send[last++] = 127;
                byte[] len = BitConverter.GetBytes((short)(msg_byte.Length - 1));

                Array.Reverse(len);
                Array.Copy(len, 0, to_send, ++last, 2);
                last += 7;
            }

            msg_byte.CopyTo(to_send, last);
            mtx.WaitOne();
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(to_send));
            TC.GetStream().Write(to_send, 0, last + msg_byte.Length);
            mtx.ReleaseMutex();
        }
        private void recv(string msg)
        {
            if (State == WebSockState.Connecting)
            {
                if (ci.cii == null)
                {
                    ci.cii = new Dictionary<string, string>();
                    ci.cii.Add("nick", ci.nick);
                    ci.cii.Add("room", ci.room);
                }

                Send(make_msg("join", ci.cii));
                State = WebSockState.Connected;
            }
            else if (msg == "2::")
            {
                Send("2::");
            }
            else
            {
                msg = msg.Substring(4);
                Newtonsoft.Json.JsonTextReader JTR = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(msg));

                char name_type = '\0';
                Dictionary<string, string> args = new Dictionary<string, string>(5);

                while (JTR.Read())
                {
                    switch (JTR.ValueType == typeof(string) ? (string)JTR.Value : "")
                    {
                        case "name":
                            name_type = JTR.ReadAsString()[0];
                            break;
                        case "args":
                            switch (name_type)
                            {
                                case 'u':
                                    JTR.Read();
                                    JTR.Read();
                                    JTR.Read();

                                    switch (JTR.ReadAsString())
	                                {
                                        case "l":
                                            Dictionary<string, user_info> ui = new Dictionary<string,user_info>(8);
                                            JTR.Read();
                                            JTR.Read();
                                            while (JTR.Read())
                                            {
                                                string nick = (string)JTR.Value;
                                                user_info lui = new user_info();
                                                JTR.Read();
                                                while (JTR.Read() && JTR.Value != null && JTR.ValueType.ToString() != "EndObject")
                                                {
                                                    switch ((string)JTR.Value)
                                                    {
                                                        case "a":
                                                            lui.admin = JTR.ReadAsInt32() == 0 ? false : true;
                                                            break;
                                                        case "t":
                                                            lui.conn = (DateTime)JTR.ReadAsDateTime();
                                                            break;
                                                        case "m":
                                                            lui.mb_id = JTR.ReadAsString();
                                                            break;
                                                        case "l":
                                                            lui.login = JTR.ReadAsInt32() == 1 ? false : true;
                                                            break;

                                                        default:
                                                            JTR.Read();
                                                            break;
                                                    }
                                                }

                                                if(nick != null) ui.Add(nick, lui);
                                            }
                                            break;
                                        default:
                                            break;
	                                }

                                    break;

                                case 'c':
                                    JTR.Skip();
                                    JTR.Skip();

                                    while (JTR.Read())
                                    {
                                        args.Add((string)JTR.Value, JTR.ReadAsString());
                                    }
                                    break;

                                default:
                                    break;
                            }

                            break;

                        default:
                            break;
                    }
                }
            }
        }

        private void monitor()
        {
            if (TC == null || TC.Connected == false) throw new Exception("TcpClient가 초기화되지 않았거나 연결되지 않았습니다. - monitor()");

            NetworkStream NS = TC.GetStream();
            byte[] buf = new byte[2048];
            int len = -1;

            while (TC.Connected == true)
            {
                len = NS.Read(buf, 0, 2048);
                if (len == 0) break;

                if (State == WebSockState.Connecting)
                {
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n')
                        {
                            Array.Copy(buf, i + 4, buf, 0, buf.Length - 5 - i - 4);
                        }
                    }
                }

                string[] frame = frame_cut(buf, len);
                for (int i = 0; i < frame.Length; i++)
                {
                    Console.WriteLine(frame[i]);
                    recv(frame[i]);
                }
            }
        }
        private byte[] frame_cut_reverse_len_byte(ref byte[] buf, ref int last, byte len)
        {
            byte[] to_return = new byte[len];
            while(len > 0)
            {
                --len;
                to_return[len] = buf[++last];
            }

            return to_return;
        }
        private string[] frame_cut(byte[] msg, int len)
        {
            int last = 0;
            List<string> to_return = new List<string>(5);
            while (len > last++)
            {
                if (msg[last] == 0) continue;

                if (msg[last] < 126)
                {
                    byte tlen = msg[last];
                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, tlen));
                    
                    last += tlen;
                }
                else if (msg[last] == 126)
                {
                    ushort tlen = BitConverter.ToUInt16(frame_cut_reverse_len_byte(ref msg, ref last, 2), 0);
                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, tlen));
                    
                    last += tlen;
                }
                else
                {
                    double tlen = BitConverter.ToDouble(frame_cut_reverse_len_byte(ref msg, ref last, 8), 0);
                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, (int)tlen));

                    last += (int)tlen;
                }
            }

            return to_return.ToArray();
        }
        private string make_msg(string name, Dictionary<string, string> args)
        {
            SB.Clear();
            SB.Append("5:::{\"name\":\"");
            SB.Append(name);
            SB.Append("\", \"args\":[{");
            foreach (KeyValuePair<string, string> item in args)
            {
                SB.Append("\"");
                SB.Append(item.Key);
                SB.Append("\":\"");
                SB.Append(item.Value);
                SB.Append("\",");
            }
            SB.Append("\"esm_bot\":\"1\"}]}");

            return SB.ToString();
        }

        private string time_token(string port)
        {
            byte retry = 0;
            string str = "";

            while (str == "")
            {
                try
                {
                    str = new System.Net.WebClient().DownloadString("http://chat.uchat.co.kr:" + port + "/socket.io/1/?t=" + ((new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Millisecond).Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / 10000L).ToString());
                }
                catch { System.Threading.Thread.Sleep(1000); if(++retry == 10) throw new Exception("토큰을 받아올 수 없습니다. (시도횟수 10) - time_token()"); }
            }

            return str.Substring(0, str.IndexOf(":"));
        }
    }
}
