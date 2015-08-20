using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpeedLogger4UChat
{
    enum WebSockState
	{
	         Connecting, Connected, Closed
	}

    public class UChatWebSocket
    {
        private System.Net.Sockets.TcpClient TC;
        private System.Net.Sockets.NetworkStream NS;
        private System.Threading.Thread Recv_Thread;
        private WebSockState State;
        private System.Threading.Mutex mtx;
        
        public UChatWebSocket()
        {
            TC = new System.Net.Sockets.TcpClient();
            mtx = new System.Threading.Mutex();
        }

        public void Connect(int port)
        {
            if (TC.Connected == true) throw new Exception("이미 연결되어 있습니다.");
            TC.Connect("chat.uchat.co.kr", port);

            byte[] buf = System.Text.Encoding.UTF8.GetBytes("GET /socket.io/1/websocket/"+time_token(port.ToString())+" HTTP/1.1\r\nHost: chat.uchat.co.kr:"+port.ToString()+"\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nUser-Agent: UChat_Bot\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nOrigin: http://esukmean.com\r\nSec-WebSocket-Version: 13\r\n\r\n");
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(buf));
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
            //if (State == WebSockState.Connecting) throw new Exception("소켓을 연결중입니다.");

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
                to_send[last++] = len[1];
                to_send[last++] = len[0];
            }
            else {

                to_send[last++] = 127;
                byte[] len = BitConverter.GetBytes((short)(msg_byte.Length - 1));
                to_send[last++] = len[7];
                to_send[last++] = len[6];
                to_send[last++] = len[5];
                to_send[last++] = len[4];
                to_send[last++] = len[3];
                to_send[last++] = len[2];
                to_send[last++] = len[1];
                to_send[last++] = len[0];
            }

            msg_byte.CopyTo(to_send, last);
            mtx.WaitOne();
            TC.GetStream().Write(to_send, 0, last + msg_byte.Length);
            mtx.ReleaseMutex();
        }
        private void recv(string msg)
        {
            Console.WriteLine(msg);
            if (State == WebSockState.Connecting)
            {
                Send("1::");
                State = WebSockState.Connected;
            }
            else if (msg == "2::")
            {
                Send("2::");
            }
            else
            {
                Send("5:::{\"name\":\"join\",\"args\":[{\"room\":\"basic\",\"nick\":\"ASDFFFF(9abdb)\",\"mb_id\":\"\",\"md5\":\"\",\"chat_record\":true,\"chat_record_count\":15}]}");
            }
        }

        private void monitor()
        {
            if (TC == null) throw new Exception("TcpClient가 초기화되지 않았습니다. - monitor()");

            System.Net.Sockets.NetworkStream NS = TC.GetStream();
            byte[] buf = new byte[2048];
            int len = -1;
            while (TC.Connected == true)
            {
                len = NS.Read(buf, 0, 2048);
                if (len == 0) break;
                if (State == WebSockState.Connected)
                {
                    string[] frame = frame_cut(buf, len);
                    for (int i = 0; i < frame.Length; i++)
                    {
                        recv(frame[i]);
                    }
                }
                else if(State == WebSockState.Connecting)
                {
                    recv("");
                }
            }
        }
        private string[] frame_cut(byte[] msg, int len)
        {
            int last = 0;
            List<string> to_return = new List<string>(5);
            while (len > last++)
            {
                if (msg[last] < 126)
                {
                    byte tlen = msg[last];
                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, tlen));
                    
                    last += tlen;
                }
                else if (msg[last] == 126)
                {
                    byte[] len_tmp = new byte[2];
                    len_tmp[1] = msg[++last];
                    len_tmp[0] = msg[++last];
                    ushort tlen = BitConverter.ToUInt16(len_tmp, 0);
                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, tlen));
                    
                    last += tlen + 3;
                }
                else
                {
                    byte[] len_tmp = new byte[8];
                    len_tmp[7] = msg[++last];
                    len_tmp[6] = msg[++last];
                    len_tmp[5] = msg[++last];
                    len_tmp[4] = msg[++last];
                    len_tmp[3] = msg[++last];
                    len_tmp[2] = msg[++last];
                    len_tmp[1] = msg[++last];
                    len_tmp[0] = msg[++last];
                    double tlen = BitConverter.ToDouble(len_tmp, 0);

                    to_return.Add(System.Text.Encoding.UTF8.GetString(msg, ++last, (int)tlen));

                    last += (int)tlen;
                }
            }

            return to_return.ToArray();
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
