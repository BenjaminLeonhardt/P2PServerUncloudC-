using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace P2PServerFormsGui {



    public partial class Form1 : Form {

        public delegate void AddListItem();
        public AddListItem myDelegate;
        public Form1() {
            InitializeComponent();
            myDelegate = new AddListItem(AddListItemMethod);
            peersListe.FullRowSelect = true;
        }

        private void AddListItemMethod() {
            peersListe.Items.Clear();
            foreach (Peer item in Server.Peers) {
                ListViewItem newItem = new ListViewItem(item.id);
                newItem.SubItems.Add(item.name);
                newItem.SubItems.Add(item.ip);
                newItem.SubItems.Add("Windows 10");

                peersListe.Items.Add(newItem);
            }
        }

        public Thread ListenThread { get; set; }
        static byte[] buffer { get; set; }

       

        static Socket socket;


        private void start_Click(object sender, EventArgs e) {

            string HostName = System.Net.Dns.GetHostName();

            IPHostEntry hostInfo = Dns.GetHostEntry(HostName);
            string IpAdresse = hostInfo.AddressList[hostInfo.AddressList.Length - 1].ToString();

            Server server = new P2PServerFormsGui.Server(10, 1024,this);
            server.Init();
            server.Start(new IPEndPoint(IPAddress.Any, 5000));


            //socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //socket.Bind(new IPEndPoint(IPAddress.Any, 5000));
            //socket.Listen(100);

            //ThreadStart start = new ThreadStart(accept);
            //Thread ListenThread = new Thread(start);
            //ListenThread.IsBackground = true;
            //ListenThread.Start();


            StatusText.Text = IpAdresse + ":5000";
            StatusText.ForeColor = Color.Green;



        }
        public void accept() {
            try {
                Socket accepted = socket.Accept();
                while (true) {

                    buffer = new byte[accepted.SendBufferSize];
                    int bytesRead = accepted.Receive(buffer);
                    byte[] formated = new byte[bytesRead];
                    for (int i = 0; i < bytesRead; i++) {
                        formated[i] = buffer[i];
                    }
                    string strData = Encoding.ASCII.GetString(formated);
                    Console.Write(strData + Environment.NewLine);
                    Thread.Sleep(50);
                }
            }catch(Exception ex) {
                Console.WriteLine(ex.ToString());
                accept();
            }
            

        }

        private void peersListe_SelectedIndexChanged(object sender, EventArgs e) {
            
        }
    }
}
