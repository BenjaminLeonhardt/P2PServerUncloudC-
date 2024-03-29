﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using P2PServerFormsGui;

namespace P2PServerFormsGui {
    class Server {

        private int m_numConnections;   // the maximum number of connections the sample is designed to handle simultaneously 
        private int m_receiveBufferSize;// buffer size to use for each socket I/O operation 
        BufferManager m_bufferManager;  // represents a large reusable set of buffers for all socket operations
        const int opsToPreAlloc = 2;    // read, write (don't alloc buffer space for accepts)
        Socket listenSocket;            // the socket used to listen for incoming connection requests
                                        // pool of reusable SocketAsyncEventArgs objects for write, read and accept socket operations
        SocketAsyncEventArgsPool m_readWritePool;
        int m_totalBytesRead;           // counter of the total # bytes received by the server
        int m_numConnectedSockets;      // the total number of clients connected to the server 
        Semaphore m_maxNumberAcceptedClients;
        public static List<Peer> Peers = new List<Peer>(); //Liste aller verbundenen Peers
        public static int ID = 1;                           //ID Counter für die Peers

        Form1 mainForm ;
        // Create an uninitialized server instance.  
        // To start the server listening for connection requests
        // call the Init method followed by Start method 
        //
        // <param name="numConnections">the maximum number of connections the sample is designed to handle simultaneously</param>
        // <param name="receiveBufferSize">buffer size to use for each socket I/O operation</param>
        public Server(int numConnections, int receiveBufferSize, Form1 _mainForm) {
            mainForm = _mainForm;
            m_totalBytesRead = 0;
            m_numConnectedSockets = 0;
            m_numConnections = numConnections;
            m_receiveBufferSize = receiveBufferSize;
            // allocate buffers such that the maximum number of sockets can have one outstanding read and 
            //write posted to the socket simultaneously  
            m_bufferManager = new BufferManager(receiveBufferSize * numConnections * opsToPreAlloc,
                receiveBufferSize);

            m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
            m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
        }

        // Initializes the server by preallocating reusable buffers and 
        // context objects.  These objects do not need to be preallocated 
        // or reused, but it is done this way to illustrate how the API can 
        // easily be used to create reusable objects to increase server performance.
        //
        public void Init() {
            // Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds 
            // against memory fragmentation
            m_bufferManager.InitBuffer();

            // preallocate pool of SocketAsyncEventArgs objects
            SocketAsyncEventArgs readWriteEventArg;

            for (int i = 0; i < m_numConnections; i++) {
                //Pre-allocate a set of reusable SocketAsyncEventArgs
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
                readWriteEventArg.UserToken = new Peer();

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                m_bufferManager.SetBuffer(readWriteEventArg);

                // add SocketAsyncEventArg to the pool
                m_readWritePool.Push(readWriteEventArg);
            }

        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        //
        // <param name="localEndPoint">The endpoint which the server will listening 
        // for connection requests on</param>
        public void Start(IPEndPoint localEndPoint) {
            // create the socket which listens for incoming connections
            listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(localEndPoint);
            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(10);

            // post accepts on the listening socket
            StartAccept(null);

            //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
            Console.WriteLine("Press any key to terminate the server process....");
            //Console.ReadKey();
        }


        // Begins an operation to accept a connection request from the client 
        //
        // <param name="acceptEventArg">The context object to use when issuing 
        // the accept operation on the server's listening socket</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg) {
            if (acceptEventArg == null) {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            } else {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            m_maxNumberAcceptedClients.WaitOne();
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent) {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync 
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e) {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e) {
            Interlocked.Increment(ref m_numConnectedSockets);
            Console.WriteLine("Client connection accepted. There are {0} clients connected to the server",
                m_numConnectedSockets);

            // Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
            ((Peer)readEventArgs.UserToken).Socket = e.AcceptSocket;

            // As soon as the client is connected, post a receive to the connection
            bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
            if (!willRaiseEvent) {
                ProcessReceive(readEventArgs);
            }

            // Accept the next connection request
            StartAccept(e);
        }

        // This method is called whenever a receive or send operation is completed on a socket 
        //
        // <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
        void IO_Completed(object sender, SocketAsyncEventArgs e) {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation) {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }

        }

        // This method is invoked when an asynchronous receive operation completes. 
        // If the remote host closed the connection, then the socket is closed.  
        // If data was received then the data is echoed back to the client.
        //
        private void ProcessReceive(SocketAsyncEventArgs e) {
            // check if the remote host closed the connection
            Peer token = (Peer)e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success) {
                //increment the count of the total bytes receive by the server
                Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                //Console.WriteLine("The server has read a total of {0} bytes", m_totalBytesRead);
                byte[] tmp = new byte[e.BytesTransferred+1];
                int j = 0;
                for(int i= e.Offset;i< e.Offset+ e.BytesTransferred; i++) {
                    tmp[j] = e.Buffer[i];
                    j++;
                }
                string strTmp = Encoding.ASCII.GetString(tmp);
                int beginMessage = -1;
                int endMessage = -1;
                int laengeMessage = -1;
                for (int i = 0; i < strTmp.Length; i++) {
                    if(strTmp[i] == 'b' && strTmp[i+1] == 'e' && strTmp[i+2] == 'g' && strTmp[i+3] == '{') {
                        beginMessage = i + 4;
                    }
                }
                for (int i = 0; i < strTmp.Length; i++) {
                    if (strTmp[i] == '}' && strTmp[i + 1] == 'e' && strTmp[i + 2] == 'n' && strTmp[i + 3] == 'd') {
                        endMessage = i;
                    }
                }
                laengeMessage = endMessage - beginMessage;
                Console.WriteLine("empfangen: " + strTmp);
                if(!(beginMessage <= -1 || endMessage <= -1 || laengeMessage <= -1)) {
                    strTmp = strTmp.Substring(beginMessage, laengeMessage);

                    if (strTmp[0] == '1') {
                        int indexOfName = strTmp.IndexOf(':', 2) - 2;
                        string name = strTmp.Substring(2, indexOfName);
                        int begin = strTmp.IndexOf(':', indexOfName + 1) + 1;
                        int length = (strTmp.IndexOf('\n')) - (begin);
                        if (strTmp.IndexOf('\n') <= 0) {
                            length = strTmp.Length - begin;
                        }
                        string ip ="";
                        try {
                            ip = strTmp.Substring(begin, length);
                        } catch (Exception ex) {
                            Console.WriteLine(ex.ToString());
                        }
                       
                        bool gefunden = false;
                        foreach (Peer item in Peers) {
                            if (item.ip.Equals(ip)) {
                                gefunden = true;
                            }
                        }
                        if (!gefunden) {
                            Peer peer = new Peer();
                            peer.name = name;
                            peer.ip = ip;
                            peer.id = ID++.ToString();
                            peer.os = "";
                            Peers.Add(peer);
                            mainForm.Invoke(mainForm.myDelegate);
                        }
                    }

                    Console.WriteLine("Empfangen: {0}", strTmp);
                    string stringBuffer = "beg{";
                    foreach (Peer item in Peers) {
                        stringBuffer += item.id + ":" + item.name + ":" + item.ip + ":" + item.os + "\n";
                    }
                    stringBuffer += "}end";
                    Console.WriteLine("send back to peer: {0}", stringBuffer);
                    byte[] buf = new byte[stringBuffer.Length];
                    buf = Encoding.ASCII.GetBytes(stringBuffer);

                    //send a string with all peers data back to client
                    e.SetBuffer(buf, 0, stringBuffer.Length);
                }else {
                    string stringBuffer = " ";
                    byte[] buf = new byte[stringBuffer.Length];
                    e.SetBuffer(buf, 0, stringBuffer.Length);
                }
                //echo the data received back to the client
                //e.SetBuffer(e.Offset, e.BytesTransferred);
                bool willRaiseEvent = token.Socket.SendAsync(e);
                if (!willRaiseEvent) {
                    ProcessSend(e);
                }

            } else {
                CloseClientSocket(e);
            }
        }

        // This method is invoked when an asynchronous send operation completes.  
        // The method issues another receive on the socket to read any additional 
        // data sent from the client
        //
        // <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success) {
                // done echoing data back to the client
                Peer token = (Peer)e.UserToken;
                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent) {
                    ProcessReceive(e);
                }
            } else {
                CloseClientSocket(e);
            }
        }

        private void CloseClientSocket(SocketAsyncEventArgs e) {
            Peer token = e.UserToken as Peer;
            string tmp = token.Socket.RemoteEndPoint.ToString();
            string ipString = tmp.Substring(0, tmp.IndexOf(':'));

            foreach (Peer item in Peers) {
                if (item.ip.Equals(ipString)) {
                    Peers.Remove(item);
                    break;
                }
                
            }
            mainForm.Invoke(mainForm.myDelegate);
            // close the socket associated with the client
            try {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
            token.Socket.Close();

            // decrement the counter keeping track of the total number of clients connected to the server
            Interlocked.Decrement(ref m_numConnectedSockets);

            // Free the SocketAsyncEventArg so they can be reused by another client
            m_readWritePool.Push(e);

            m_maxNumberAcceptedClients.Release();
            Console.WriteLine("A client has been disconnected from the server. There are {0} clients connected to the server", m_numConnectedSockets);
        }
    }
}
