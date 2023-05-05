using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Collections.Concurrent;
using Entidades.Comunicacion;
using Newtonsoft.Json;
using System.Linq;

namespace ModuloPantallaTeclado.Clases
{
    // State object for reading client data asynchronously  
    public class StateObject
    {
        // Client  socket.  
        public Socket workSocket = null;
        // Size of receive buffer.  
        public const int BufferSize = 1024;
        // Receive buffer.  
        public byte[] buffer = new byte[BufferSize];
        // Received data string.  
        public StringBuilder sb = new StringBuilder();
    }

    public class AsynchronousSocketListener
    {
        private static bool _estoyEscuchando = true;
        private static bool _clienteConectado = false;
        // Thread signal.  
        public static ManualResetEvent allDone = new ManualResetEvent(false);
        private static int _puertoTCP;
        private static List<Socket> _listaSockets = new List<Socket>();
        private static Socket _listener = null;
        // Construct a ConcurrentQueue.
        private static ConcurrentQueue<string> _jsonCommandQueue = new ConcurrentQueue<string>();
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");

        #region Delegado que procesa el cambio en la conexion
        // Events that applications can receive
        public static event ProcesarCambioConexion_Delegado ProcesarCambioEstadoConexion;
        // Delegated functions (essentially the function prototype)
        public delegate void ProcesarCambioConexion_Delegado();
        #endregion

        #region Delegado que procesa los datos recibidos desde logica
        // Events that applications can receive
        public static event ProcesarDatosRecibidos_Delegado ProcesarDatosRecibidos;
        // Delegated functions (essentially the function prototype)
        public delegate void ProcesarDatosRecibidos_Delegado(ComandoLogica comandoJson);
        #endregion

        public AsynchronousSocketListener()
        {
          
        }

        public static void Abrir()
        {
            //Marco el flag de que acepte todas las conexiones que llegan
            _estoyEscuchando = true;
        }

        public static void Cerrar()
        {
            _logger.Trace("Close - Inicio");
            try
            {
                //Marco el flag de que cierre todas las conexiones que llegan
                _estoyEscuchando = false;
                // Signal the main thread to continue.  
                allDone.Set();

                //Si existe el socket lo cierro
                if (_listener != null)
                    _listener.Close();

                //Para todas las conexiones abiertas, las cierro
                foreach (Socket handler in _listaSockets)
                {
                    handler.Shutdown(SocketShutdown.Both);
                }
                //No vacio la lista porque al cerrar cada socket ya se saca de la lista.
            }
            catch(Exception ex)
            {
                _logger.Warn("AsynchronousSocketListener : Close exception [{0}]", ex.ToString());
            }
            _logger.Trace("Close - Fin");
        }
        public static void StartListening()
        {
            _estoyEscuchando = true;
            // Establish the local endpoint for the socket.  
            try
            {
                string port = ConfigurationManager.AppSettings["Puerto"];
                if (string.IsNullOrEmpty(port))
                {
                    _puertoTCP = 12006;
                }
                else
                {
                    int nro;
                    bool success = int.TryParse(port, out nro);
                    if (!success)
                        _puertoTCP = 12006;
                    else
                        _puertoTCP = nro;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("AsynchronousSocketListener:StartListening() Exception: {0}", ex.Message.ToString());
            }

            IPAddress ipAddress = IPAddress.Any;

            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _puertoTCP);

            // Create a TCP/IP socket.  
            _listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            bool puertoTcpAbierto = true;
            while (_estoyEscuchando)
            {
                // Bind the socket to the local endpoint and listen for incoming connections.  
                try
                {
                    _listener.Bind(localEndPoint);
                    _listener.Listen(100);

                    puertoTcpAbierto = true;
                    while (_estoyEscuchando)
                    {
                        // Set the event to nonsignaled state.  
                        allDone.Reset();
                        // Start an asynchronous socket to listen for connections.  
                        _listener.BeginAccept(new AsyncCallback(AcceptCallback), _listener);
                        // Wait until a connection is made before continuing.  
                        allDone.WaitOne();
                    }
                }
                catch (Exception)
                {
                    if (puertoTcpAbierto)
                    {
                        puertoTcpAbierto = false;
                    }
                }
                Thread.Sleep(1000);
            }
        }

        public static bool PuertoAbierto()
        {
            return _clienteConectado;
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            _logger.Info("AcceptCallback - Inicio");

            try
            {
                // Signal the main thread to continue.  
                allDone.Set();

                // Get the socket that handles the client request.  
                Socket _listener = (Socket)ar.AsyncState;
                Socket handler = _listener.EndAccept(ar);
                
                if (_estoyEscuchando)
                {
                    if (_listaSockets.Any())
                        _listaSockets.Clear();
                       
                    _listaSockets.Add( handler );

                    _logger.Info( "AsynchronousSocketListener: AcceptCallback: Se conecto cliente [{0}]", handler.RemoteEndPoint.ToString() );
                    _clienteConectado = true;
                    //ProcesarCambioEstadoConexion?.Invoke();

                    // Create the state object.  
                    StateObject state = new StateObject();
                    state.workSocket = handler;

                    handler.BeginReceive( state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback( ReadCallback ), state );
                }
                else
                {
                    //Si frenaron el servicio, me quedo escuchando pero cierro las conexiones
                    handler.Shutdown(SocketShutdown.Both);
                }
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception ex)
            {
                _logger.Warn("AsynchronousSocketListener:AcceptCallback() Exception: {0}", ex.Message.ToString());
            }
            _logger.Info("AcceptCallback - Fin");
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            string content = string.Empty;
            // Retrieve the state object and the handler socket  
            // from the asynchronous state object.  
            StateObject state = (StateObject)ar.AsyncState;
            Socket handlerSocket = state.workSocket;

            int bytesRead = 0;
            // Read data from the client socket.   
            try
            {
                bytesRead = handlerSocket.EndReceive(ar);
            }
            catch (Exception ex)
            {
                _logger.Warn("AsynchronousSocketListener:ReadCallback() 1 Exception: {0}", ex.Message.ToString());
            }

            if (bytesRead > 0)
            {
                // There  might be more data, so store the data received so far.  
                state.sb.Append(Encoding.Default.GetString(state.buffer, 0, bytesRead));

                // Check for end-of-file tag. If it is not there, read   
                // more data.  
                content = state.sb.ToString();

                if (content.IndexOf("\n") > -1)
                {
                    /*
                    var comandos = content.Split(new string[] { "\n" }, 255, StringSplitOptions.RemoveEmptyEntries);                    
                    foreach (string comando in comandos)
                    {
                        if (comando != "")
                        {
                            try
                            {
                                ComandoLogica com = JsonConvert.DeserializeObject<ComandoLogica>(comando);
                                ProcesarDatosRecibidos(com);
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.Debug("AsynchronousSocketListener:ReadCallback() JsonException: {0}", jsonEx.Message.ToString());
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug("AsynchronousSocketListener:ReadCallback() Exception: {0}", ex.Message.ToString());
                            }
                        }
                    }
                    */

                    // Chequea que el ultimo caracter sea '\n'
                    bool comandoFinalCompleto = content.Substring( content.Length - 1, 1 ) == "\n";

                    string [] stringVector = content.Split( '\n' );

                    // Para saber si ejecuta o no el ultimo comando
                    int limiteSuperior = comandoFinalCompleto ? stringVector.Length : stringVector.Length - 1;

                    for( int i = 0; i < limiteSuperior; i++ )
                    {
                        string comando = stringVector[i];

                        if( comando != "" )
                        {
                            try
                            {
                                ComandoLogica com = JsonConvert.DeserializeObject<ComandoLogica>( comando );
                                ProcesarDatosRecibidos( com ); 
                            }
                            catch( JsonException jsonEx )
                            {
                                _logger.Warn( "AsynchronousSocketListener:ReadCallback() JsonException: {0}", jsonEx.Message.ToString() );
                            }
                            catch( Exception ex )
                            {
                                _logger.Warn( "AsynchronousSocketListener:ReadCallback() 2 Exception: {0}", ex.Message.ToString() );
                            }
                        } 
                    }

                    //Borro el buffer
                    state.sb.Clear();

                    //Chequeo si tengo algun comando que quedo pendiente
                    if( !comandoFinalCompleto )
                        state.sb.Append( stringVector[stringVector.Length - 1] );
                }


                // Not all data received. Get more.
                try
                {
                    handlerSocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                }
                catch (Exception ex)
                {
                    //Se desconectó el cliente, saco el socket de la lista
                    _listaSockets.Remove(state.workSocket);
                    _logger.Warn("AsynchronousSocketListener:ReadCallback() 3 Exception: {0}", ex.Message.ToString());
                }
            }
            else
            {
                //Me desconectaron, saco el socket de la lista
                _listaSockets.Remove(state.workSocket);
                _logger.Info("AsynchronousSocketListener: ReadCallback: Se desconecto cliente [{0}]", handlerSocket.RemoteEndPoint.ToString());
                _clienteConectado = false;
                ProcesarCambioEstadoConexion?.Invoke();
            }

        }

        public static void SendDataToAll(ComandoLogica datoJson)
        {
            //Solo envio datos si el puerto esta abierto
            //if (PuertoAbierto())
            {
                string data = JsonConvert.SerializeObject(datoJson);
                //si no hay nada en la lista de socket, no enviar
                if (_listaSockets != null && _listaSockets.Any())
                {
                    _logger.Info("SocketServidor:SendDataToAll() Status[{0}] Accion[{1}]",datoJson.DescripcionStatus, datoJson.Accion);
                    foreach (Socket sock in _listaSockets)
                    {
                        Send(sock, string.Format("{0}\n", data));
                    }
                }
                _logger.Debug("SendDataToAll -> No hay clientes. Accion[{0}]",datoJson.Accion);
            }
        }

        private static void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.  
            byte[] byteData = Encoding.Default.GetBytes(data);
            // Begin sending the data to the remote device.  
            handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                Socket handler = (Socket)ar.AsyncState;
                // Complete sending the data to the remote device.  
                int bytesSent = handler.EndSend(ar);
            }
            catch (Exception ex)
            {
                _logger.Warn("AsynchronousSocketListener:SendCallback() Exception: {0}", ex.Message.ToString());
            }
        }


    }
}
