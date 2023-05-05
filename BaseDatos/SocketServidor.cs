using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using Entidades.ComunicacionBaseDatos;
using Entidades;
using ModuloBaseDatos.Entidades;
using Entidades.ComunicacionMonitor;
using System.Data.SqlTypes;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Clase que inicializa el objeto para leer la data del cliente de forma asincrónica.
    /// </summary>
    /// ****************************************************************************************************
    public class StateObject
    {
        //Socket cliente.  
        public Socket workSocket = null;
        //Tama#o del buffer receptor..  
        public const int BufferSize = 1024;
        //Buffer receptor.  
        public byte[] buffer = new byte[BufferSize];
        //String que recibe la data.  
        public StringBuilder sb = new StringBuilder();
        //Cronometro
        private Stopwatch sw;

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa el cronometro.
        /// </summary>
        /// ************************************************************************************************
        public void StartWatch()
        {
            sw = new Stopwatch();
            sw.Reset();
            sw.Start();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Detiene el cronometro y devuelve los milisegundos transcurridos.
        /// </summary>
        /// ************************************************************************************************
        public long StopWatch()
        {
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        public long ElapsedWatch()
        {
            return sw.ElapsedMilliseconds;
        }
    }

    /// ****************************************************************************************************
    /// <summary>
    /// Clase que contiene los métodos correspondientes al Socket.
    /// </summary>
    /// ****************************************************************************************************
    public class AsynchronousSocketListener
    {
        private IBaseDatos _base;
        private ConfigListas _conList;
        private static GestionComandos _gstComandos;
        private static CheckConnection _checkConnection;
        private EnvioEventos _envioEve = new EnvioEventos();
        private GestionTurno _turnoGst = new GestionTurno();
        private SolicitudBaseDatos _getComandos = new SolicitudBaseDatos(), _getFechaHora = new SolicitudBaseDatos();
        private Utility _util;
        private static int _modo, _via, _est, _intervaloConn = 10000, _intervaloRetry = 3000;
        private static string _uid;
        private static eEstadoRed _statConn;
        private static Socket _listener = null;
        private static List<Socket> _listaSockets = new List<Socket>();
        private static List<Socket> _listaSocketsAlert = new List<Socket>();
        private static ManualResetEvent _allDone = new ManualResetEvent(false), _waitConfig = new ManualResetEvent(false);
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private static System.Timers.Timer _actualizacionesTimer, _initTimer, _checkConnTimer, _borrarTimer, _comandosTimer, _fechaHoraTimer, _dataSizeTimer;
        private static bool _estoyEscuchando = true, _initUpdate = false, _initFlag = false, _tengoConfiguracion = false;
        private static bool _recibiendoComandos = false, _reintentandoListas = false, _buscandoActualizaciones = false, _envioFechaHora = false;
        private static bool _estoyChequeandoCon = false, _cambiePass = false, _tengoListasCargadas = false, _primerReporteBD = true;
        private static DateTime _dtRecibiendoComandos = new DateTime(2000, 1, 1), _ultimoCheckCon = new DateTime(2000, 1, 1);
        private const string CAMBIOHORARIOSP = "ViaNet.usp_sincronizacionHoraria";
        private const double MILLISECONDS = 3600 * 24000;

        //KeepAlive
        private static Dictionary<int, ErrorKeepAlive> _respuestas = new Dictionary<int, ErrorKeepAlive>();
        private static DateTime _ultimaRespuesta;
        private static EnmErrorBaseDatos _ultimoEstado;
        private static RespuestaKAMonitor _respuestaKA = new RespuestaKAMonitor();
        private static object _lockRespuestaKAMonitor = new object();

        /// ************************************************************************************************
        /// <summary>
        /// Comienza a comprobar si hay conexión a la BD antes de actualizar las listas.
        /// </summary>
        /// ************************************************************************************************
        public void ActualizaLocal()
        {
            _initFlag = true;

            /*if (Configuraciones.Instance.Configuracion.MotorBd == MotorBaseDatos.SQLITE.ToString())
            {
                _base = new SQLite(Configuraciones.Instance.Configuracion);
            }
            else*/
            if (Configuraciones.Instance.Configuracion.MotorBd == MotorBaseDatos.SQLEXPRESS.ToString())
            {
                _base = new SQLExpress(Configuraciones.Instance.Configuracion);
            }
            else if (Configuraciones.Instance.Configuracion.MotorBd == MotorBaseDatos.SQLEXPRESS2014.ToString())
            {
                //_base = new SQLExpress2014(Configuraciones.Instance.Configuracion);
            }

            _util = new Utility();
            _conList = new ConfigListas();
            _checkConnection = new CheckConnection();
            _tengoListasCargadas = _conList.InicioTablaBaseDatos();
            _base.BuscarConfiguracion("BUSCAR");

            _waitConfig.WaitOne(); //Espero a que la vía solicite la configuración

            GestionTurno.ChequearTurno();
            _conList.Init();
            _checkConnection.Init();
            //envio a logica si hay o no datos locales
            _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, _checkConnection.StatusConexion().ToString() + "|" + _tengoListasCargadas);
            _gstComandos = new GestionComandos(_conList);
            _modo = Configuraciones.Instance.Configuracion.Modo;

            if(_modo == 3 || _modo == 2)
                EnvioEventos.Init(Configuraciones.Instance.Configuracion);

            SetTimerCon();
            SetTimerFechaHora();
            SetTimerDataSize();
        }

        #region Conexión y Desconexión
        /// ************************************************************************************************
        /// <summary>
        /// El socket comienza a escuchar.
        /// </summary>
        /// ************************************************************************************************
        public void StartListening()
        {
            _estoyEscuchando = true;
            LlenarDictionary();
            _ultimaRespuesta = DateTime.Now;
            //Establece el endpoint local para el socket
            IPAddress ipAddress = IPAddress.Any;
            IPEndPoint localEndPoint;

            bool listenerOk = false;

            try
            {
                localEndPoint = new IPEndPoint(ipAddress, Configuraciones.Instance.Configuracion.PortNumber);
            }
            catch
            {
                //Si se produce error en el puerto indicado abro por defecto el 12001
                localEndPoint = new IPEndPoint(ipAddress, 12001);
            }

            //Crea socket TCP/IP.  
            _listener = new Socket(ipAddress.AddressFamily,SocketType.Stream, ProtocolType.Tcp);

            //Une el socket al endpoint local y escucha las conexiones entrantes
            do
            {
                try
                {
                    _listener.Bind(localEndPoint);
                    _listener.Listen(100);
                    listenerOk = true;
                    _logger.Info("El socket se encuentra escuchando a posibles solicitudes...");

                    while (_estoyEscuchando)
                    {
                        //Establece el estado del evento a uno no señalizado.
                        _allDone.Reset();

                        //Inicia el socket asincrónico para escuchar conexiones.
                        _listener.BeginAccept(new AsyncCallback(AcceptCallback), _listener);

                        //Espera hasta que se haga una conexión antes de continuar.
                        _allDone.WaitOne();
                    }
                }
                catch (SocketException e)
                {
                    _logger.Error("Exception: {0}", e.Message.ToString());
                    _logger.Warn("El socket se encuentra ocupado. Espero 3 segundos y reintento");
                    listenerOk = false;

                    Thread.Sleep(3000);
                }
            }//Sigue intentando conectarse al socket si está ocupado.
            while (listenerOk == false);
        }

        /// ************************************************************************************************
        /// <summary>
        /// Indica al thread que continue procesando, estableciendo la conexión con el cliente y,
        /// que comience la lectura asincrónica de la data del cliente.
        /// </summary>
        /// <param name="ar"></param>
        /// ************************************************************************************************
        public void AcceptCallback(IAsyncResult ar)
        {
            try
            {
                //Se detuvo la aplicación... ya no estoy escuchando
                if (!_estoyEscuchando)
                    return;

                //Indica al thread principal que continúe.  
                _allDone.Set();

                //Obtiene el socket que maneja la solicitud del cliente.
                Socket client = (Socket)ar.AsyncState;
                Socket handler = client.EndAccept(ar);

                if (_estoyEscuchando)
                {
                    _listaSockets.Add(handler);
                        
                    //Crea el State Object  
                    StateObject state = new StateObject();
                    state.workSocket = handler;
                    handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                }
                else
                {
                    //Si frenaron el servicio, me quedo escuchando pero cierro las conexiones
                    handler.Shutdown(SocketShutdown.Both);
                }
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion [{0}]", e.ToString());
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Cierra las conexiones. Cierra el socket.
        /// </summary>
        /// ************************************************************************************************
        public void Cerrar()
        {
            _logger.Trace("Entro...");
            try
            {
                //Marco el flag de que cierre todas las conexiones que llegan
                _estoyEscuchando = false;

                //Si existe el socket lo cierro
                if (_listener != null)
                {
                    Socket tmp = _listener;
                    _listener = null;
                    tmp.Close();
                }

                //Indica al thread principal que continúe.  
                _allDone.Set();

                //Terminé de usar los timers
                DisposeTimers();

                //Para todas las conexiones abiertas, las cierro
                foreach (Socket handler in _listaSockets)
                {
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }

                _listaSockets.Clear();
                _listaSocketsAlert.Clear();

                _logger.Info("Cierro todas las conexiones Sockets[{0}], Alert[{1}]",_listaSockets.Count,_listaSocketsAlert.Count);
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion {0}", e.ToString());
            }

            _logger.Trace("Salgo...");
        }

        #endregion

        #region Recepción en interpretación de solicitudes

        /// ************************************************************************************************
        /// <summary>
        /// Retorna la data enviada por el cliente. Lee uno o mas bytes del socket del cliente
        /// en el buffer, llama a BeginReceive hasta que toda la data enviada del cliente esté completa.
        /// </summary>
        /// <param name="ar"></param>
        /// ************************************************************************************************
        public async void ReadCallback(IAsyncResult ar)
        {
            //Se detuvo la aplicación... ya no estoy escuchando
            if (!_estoyEscuchando)
                return;

            String content = String.Empty;
            string strRespuestaDB = String.Empty;
            RespuestaBaseDatos respuestaConsulta = new RespuestaBaseDatos();
            bool bCheck = false;

            //Obtiene el State object y el handler del socket desde el estado del socket asincrónico. 
            StateObject state = (StateObject)ar.AsyncState;
            Socket handlerSocket = state.workSocket;

            int bytesRead = 0;
            //Lee la data del socket del cliente.   
            try
            {
                bytesRead = handlerSocket.EndReceive(ar);
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion EndReceive [{0}]", e.Message);
            }

            if (bytesRead > 0)
            {
                try
                {
                    //Puede que haya mas data, por lo tanto guarda lo que hasta ahora se ha recibido.
                    state.sb.Append(Encoding.Default.GetString(state.buffer, 0, bytesRead));

                    //Verifico si hay un "\n" que me indique el final de la data. Si no lo encuentro sigo leyendo.
                    content = state.sb.ToString();

                    if (content.IndexOf("\n") > -1)
                    {
                        // Chequea que el ultimo caracter sea '\n'
                        bool comandoFinalCompleto = content.Substring( content.Length - 1, 1 ) == "\n";

                        //string comando = content.Substring(0, content.IndexOf("\n"));

                        string[] comandos = content.Split( '\n' );

                        // Para saber si ejecuta o no el ultimo comando
                        int limiteSuperior = comandoFinalCompleto ? comandos.Length : comandos.Length - 1;

                        for( int i = 0; i < limiteSuperior; i++ )
                        {
                            string comando = comandos[i];

                            if( comando != "" )
                            {
                                //Inicio cronómetro
                                state.StartWatch();
                                if ( KeepAlive( comando, ref _respuestaKA ) )
                                {
                                    try
                                    {
                                        state.StopWatch();
                                        lock (_lockRespuestaKAMonitor)
                                            strRespuestaDB = JsonConvert.SerializeObject(_respuestaKA);
                                        bCheck = true;
                                    }
                                    catch (JsonException e)
                                    {
                                        strRespuestaDB = string.Empty;
                                        _logger.Error("KeepAlive -> Excepcion en el JSON [{0}]", e.ToString());
                                    }
                                    catch (Exception e)
                                    {
                                        strRespuestaDB = string.Empty;
                                        _logger.Error("KeepAlive -> Excepcion [{0}]", e.ToString());
                                    }
                                }
                                else
                                {
                                    respuestaConsulta = await ProcesarSolicitud( comando, state );
                                    try
                                    {
                                        respuestaConsulta.TiempoDB = state.StopWatch();
                                        strRespuestaDB = JsonConvert.SerializeObject( respuestaConsulta );
                                    }
                                    catch( JsonException e )
                                    {
                                        strRespuestaDB = ErrorHandler.ArmaRespuestaError( EnmErrorBaseDatos.ErrorFormatoOut );
                                        _logger.Error( "Excepcion en el JSON a enviar [{0}]", e.ToString() );
                                    }
                                    catch( Exception e )
                                    {
                                        strRespuestaDB = ErrorHandler.ArmaRespuestaError( EnmErrorBaseDatos.ExcepcionProducida );
                                        _logger.Error( "Excepcion antes de enviar [{0}]", e.ToString() );
                                    }

                                    _logger.Info( "Respuesta a la vía = {0}", strRespuestaDB );
                                }

                                Send( handlerSocket, strRespuestaDB );

                                if (!bCheck)
                                {
                                    if (ConsiderarRespuesta((int)respuestaConsulta.CodError))
                                        IncrementarDictionary(respuestaConsulta.CodError);
                                }
                            } 
                        }

                        //Borro el buffer
                        state.sb.Clear();

                        if( !comandoFinalCompleto )
                            state.sb.Append( comandos[comandos.Length - 1] );


                        //Chequeo si tengo algun comando que quedó pendiente
                        //if( content.Substring(content.Length - 1, 1) != "\n")
                        //{
                        //    //state.sb.Append(comandos[comandos.Length - 1]);
                        //    state.sb.Append(content.Substring(content.IndexOf("\n") + 1));
                        //}
                    }

                    //Aún no recibe toda la data. Busca mas.
                    try
                    {
                        handlerSocket.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReadCallback), state);
                    }
                    catch (SocketException e)
                    {
                        _logger.Error("No recibió toda la data [{0}]", e.ToString());
                    }
                }
                catch (Exception e)
                {
                    _logger.Error("Excepcion General: {0}", e.ToString());
                    //Saco el socket
                    RemoverSocket(handlerSocket);
                }
            }
            else
            {
                //Me desconectaron, saco el socket de la lista
                RemoverSocket(handlerSocket);
            }
        }

        /// <summary>
        /// Remueve el socket de la lista
        /// </summary>
        /// <param name="handlerSocket">socket client</param>
        private void RemoverSocket(Socket handlerSocket)
        {
            try
            {
                if (_listaSockets.Contains(handlerSocket))
                    _listaSockets.Remove(handlerSocket);

                if (_listaSocketsAlert.Contains(handlerSocket))
                {
                    _listaSocketsAlert.Remove(handlerSocket);
                    //asumo que se desconectó la via
                    _base.ClearPools();
                }

                if (_gstComandos != null)
                    _gstComandos.NoFallaSent = false;

                _logger.Info("Se desconecto cliente [{0}]", handlerSocket.RemoteEndPoint.ToString());
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion al remover socket de la lista [{0}]", e.ToString());
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Procesa la solicitud de la vía y ejecuta la acción indicada
        /// </summary>
        /// <param name="sData"></param>La solicitud de la vía
        /// <param name="state"></param>Informacion del cliente
        /// <returns>La Respuesta a la vía</returns>
        /// ************************************************************************************************
        private async Task<RespuestaBaseDatos> ProcesarSolicitud(string sData, StateObject state)
        {
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            //Formato de la solicitud: ACCION,NOMBRETABLA,FILTRO,CLAVE,ORDEN,CAMPOSORDEN
            SolicitudBaseDatos solicitud = null;
            //Formato de la solicitud: ACCION,TIPOEVENTO,FECHA,SQLSTRING,SECUENCIA,FECHARECUPERACION
            SolicitudEnvioEventos solicitudEve = null;
            string sLog = String.Empty, sRes = String.Empty, sEvento = ""; ;
            bool bConsulta = true;

            try
            {
                //_logger.Debug("Antes de DESERIALIZAR {0}", state.ElapsedWatch());
                if(sData.Contains("AccionEvento"))
                {
                    bConsulta = false;
                    solicitudEve = new SolicitudEnvioEventos();
                    solicitudEve = JsonConvert.DeserializeObject<SolicitudEnvioEventos>(sData);//exec ViaNet.usp_setAperturaBloque

                    if (solicitudEve.Tipo == eTipoEventoBD.Online)
                        sEvento = "EstadoOnline";
                    else if (!string.IsNullOrEmpty(solicitudEve.SqlString))
                    {
                        int nFinal = solicitudEve.SqlString.IndexOf(" 0,") - 5;
                        if (nFinal > 0)
                            sEvento = solicitudEve.SqlString.Substring(5, nFinal);
                    }
                    _logger.Info("Evento de la vía: {0}/{1}/{2} TIEMPO: {3} ", solicitudEve.AccionEvento, solicitudEve.Tipo, sEvento, state.ElapsedWatch());

                }
                else if(sData.Contains("Accion"))
                {
                    solicitud = new SolicitudBaseDatos();
                    solicitud = JsonConvert.DeserializeObject<SolicitudBaseDatos>(sData);
                    _logger.Info("Consulta de la vía: {0}/{1}/{2} TIEMPO: {3} ", solicitud.Accion, solicitud.Tabla, solicitud.Filtro, state.ElapsedWatch());
                }
                else
                {
                    respuesta.CodError = EnmErrorBaseDatos.ErrorFormatoIn;
                    respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                    return respuesta;
                }
                //_logger.Debug("Despues de DESERIALIZAR {0}", state.ElapsedWatch());
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion [{0}]", e.Message);
                respuesta.CodError = EnmErrorBaseDatos.ErrorFormatoIn;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                return respuesta;
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion al convertir [{0}]", e.Message);
                respuesta.CodError = EnmErrorBaseDatos.ExcepcionProducida;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                return respuesta;
            }

            if((solicitud?.Tabla != eTablaBD.ConfiguracionDeVia && solicitud?.Tabla != eTablaBD.Vars && solicitud?.Accion != eAccionBD.Alert) && !_tengoListasCargadas)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                _logger.Warn("Aún no se encuentran cargadas las tablas necesaria en la Base de Datos, no se realizará la consulta: {0}", solicitud?.Tabla.ToString());
                return respuesta;
            }
            else if(!_tengoConfiguracion && solicitud?.Tabla != eTablaBD.ConfiguracionDeVia && solicitud?.Tabla != eTablaBD.Vars && solicitud?.Accion != eAccionBD.Alert)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                _logger.Warn("No hay configuracion, no se realizará la consulta: {0}", solicitud?.Tabla.ToString());
                return respuesta;
            }
            else if (solicitud?.Tabla == eTablaBD.Vars && !_tengoConfiguracion)
            {
                _logger.Debug("Me pidieron Vars... Seteo todo");
                _tengoConfiguracion = true;
                Configuraciones.Instance.Configuracion.Estacion = _est;
                Configuraciones.Instance.Configuracion.Via = _via;
                Configuraciones.Instance.Configuracion.UID = _uid;
                _waitConfig.Set();
            }

            if (bConsulta)
            {
                //Si es COMANDO, la vía solicita actualizar una lista
                if (solicitud.Accion == eAccionBD.Comando)
                {
                    if(_gstComandos?.SupervConn == "N")
                    {
                        respuesta.CodError = EnmErrorBaseDatos.DesconexionSuperv;
                        respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                    }
                    else
                    {
                        var tupleRes = await _conList.ActualizaPorComando(solicitud.Tabla.ToString());
                        respuesta = tupleRes.Item1;
                    }
                }
                //Si es TURNO, la vía quiere guardar información del turno actual
                else if (solicitud.Accion == eAccionBD.Turno)
                {
                    respuesta = _turnoGst.AccionVia(solicitud);

                    if(solicitud.Tabla == eTablaBD.SetApertura)
                    {
                        SolicitudBaseDatos solicitudAux = new SolicitudBaseDatos();
                        RespuestaBaseDatos res = new RespuestaBaseDatos();
                        solicitudAux.Tabla = eTablaBD.InitNum;
                        res = _turnoGst.AccionVia(solicitudAux);
                        if(res.CodError == EnmErrorBaseDatos.SinFalla)
                        {
                            solicitudAux.Tabla = eTablaBD.SetEstadoNumerador;
                            solicitudAux.Filtro = eEstadoNumeracion.NumeracionOk.ToString();
                        }

                    }
                }
                //Si es PROCEDURE la vía requiere información que no está en la BDLocal y hay que ejecutar un Stored Procedure
                else if (solicitud.Accion == eAccionBD.Procedure)
                {
                    if (_gstComandos?.SupervConn == "N")
                    {
                        respuesta.CodError = EnmErrorBaseDatos.DesconexionSuperv;
                        respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                    }
                    else
                    {
                        if (solicitud.Tabla != eTablaBD.GetUltimoTurno)
                            respuesta = await _base.ConsultaBaseEstacion(solicitud);
                        else
                        {
                            bool bCompara = solicitud.Filtro == "N" ? true : false;
                            solicitud.Filtro = "";
                            respuesta = await ConsultaUltimoTurno(solicitud, bCompara);
                        }
                    }  
                }
                //Si es ALERT, el servicio notificará a este socket los estados de conexion de la BD local y principal
                //también notificará si se actualizó o no una lista, para que se actualice la pantalla
                else if (solicitud.Accion == eAccionBD.Alert)
                {
                    _listaSocketsAlert.Add(state.workSocket);
                    respuesta.CodError = EnmErrorBaseDatos.EstadoConexion;
                    respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                    if (_waitConfig.WaitOne(0))
                        respuesta.RespuestaDB = _checkConnection.StatusConexion().ToString() + "|" + _tengoListasCargadas;
                    else
                        //inicialmente envio que está OK para que actualice los iconos
                        respuesta.RespuestaDB = eEstadoRed.Ambas.ToString() + "|" + false;
                }
                //No especificó accion
                else if (solicitud.Accion == eAccionBD.Default)
                {
                    respuesta.CodError = EnmErrorBaseDatos.ErrorAccion;
                    respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                }
                //Ninguna de las anteriores, es una consulta a la BD local
                else
                {
                    if (solicitud.Tabla == eTablaBD.ConfiguracionDeVia && !_tengoConfiguracion)
                    {
                        _logger.Debug("La via solicita la configuración...");
                        if (!string.IsNullOrEmpty(solicitud.Filtro))
                        {
                            solicitud.Filtro = Utility.DeserializarFiltro(solicitud.Filtro);
                            _logger.Debug("Deserializo filtro (Número de via y estación)...");
                            if (solicitud.Filtro.Contains(','))
                            {
                                string[] valores = solicitud.Filtro.Split(new Char[] { ',' });
                                int[] viaEst = Array.ConvertAll(valores, int.Parse);
                                _logger.Debug("Número de via[{0}] - Estación[{1}])", viaEst[1], viaEst[0]);
                                if (Configuraciones.Instance.Configuracion.Estacion == viaEst[0] && Configuraciones.Instance.Configuracion.Via == viaEst[1])
                                {
                                    _logger.Debug("La via y estacion que me piden es igual a la que tengo almacenada en configuración...");
                                    _base.Init(Configuraciones.Instance.Configuracion);
                                    if (_base.BuscarConfiguracion("CONFIGVIA"))
                                    {
                                        _logger.Debug("Tengo la configuración, seteo todo...");
                                        _tengoConfiguracion = true;
                                        _waitConfig.Set();
                                    }
                                    else
                                    {
                                        _logger.Debug("Aun no seteo nada...");
                                        Configuraciones.Instance.Clear();
                                    }
                                }
                                else
                                {
                                    int aux = Configuraciones.Instance.Configuracion.Estacion;
                                    _logger.Debug("Guardo la configuracion que me piden inicialmente...");
                                    Configuraciones.Instance.Configuracion.Estacion = viaEst[0];
                                    Configuraciones.Instance.Configuracion.Via = viaEst[1];
                                    //ejemplo: Via05101
                                    Configuraciones.Instance.Configuracion.UID = $"Via{viaEst[1].ToString().PadLeft(3, '0')}{viaEst[0].ToString().PadLeft(2, '0')}";
                                    _base.Init(Configuraciones.Instance.Configuracion);
                                    _base.BuscarConfiguracion("GUARDAR");

                                    _est = Configuraciones.Instance.Configuracion.Estacion;
                                    _via = Configuraciones.Instance.Configuracion.Via;
                                    _uid = Configuraciones.Instance.Configuracion.UID;

                                    solicitud.Filtro = "";
                                    solicitud.Accion = eAccionBD.Procedure;
                                    respuesta = await _base.ConsultaBaseEstacion(solicitud);

                                    try
                                    {
                                        ConfigVia config = JsonConvert.DeserializeObject<ConfigVia>(respuesta.RespuestaDB.Trim('[', ']'));
                                        Configuraciones.Instance.Configuracion.NumeroViaEscape = config.NumeroViaEscape;
                                    }
                                    catch(Exception e)
                                    {
                                        _logger.Error(e);
                                    }

                                    _logger.Trace("Transcurrido: {0}/{1}/{2} TIEMPO: {3} ", solicitud.Accion, solicitud.Tabla, solicitud.Filtro, state.ElapsedWatch());

                                    if (aux != viaEst[0] && aux != 0)
                                    {
                                        _logger.Debug("El numero de Est [{0}] almacenado en la BD local es diferente a la Est [{1}] que me piden", aux, viaEst[0]);
                                        if(!_cambiePass)
                                            _cambiePass = _checkConnection.UpdateLinkedServerLogin();

                                        //Si es diferente la est espero a que cargue los registros correspondientes a esa est
                                        _tengoListasCargadas = false;
                                    }

                                    Configuraciones.Instance.Clear();
                                    _logger.Debug("Consulta finaliza: {0}/{1}/{2}", solicitud.Accion, solicitud.Tabla, state.ElapsedWatch());
                                    return respuesta;
                                }                                
                            }
                            else
                                _logger.Debug("Mal formato de via y estacion...");
                        }
                        else
                            _logger.Debug("No me enviaron via ni estacion...");
                        solicitud.Filtro = "";
                    }
                    else if (solicitud.Tabla == eTablaBD.ConfiguracionDeVia && _tengoConfiguracion)
                        solicitud.Filtro = "";

                    respuesta = await _base.ConsultaBaseLocal(solicitud);

                    if(solicitud.Tabla == eTablaBD.ConfiguracionDeVia)
                    {
                        try
                        {
                            ConfigVia config = JsonConvert.DeserializeObject<ConfigVia>(respuesta.RespuestaDB.Trim('[', ']'));
                            Configuraciones.Instance.Configuracion.NumeroViaEscape = config.NumeroViaEscape;
                        }
                        catch
                        {

                        }
                    }

                    if (solicitud.Tabla == eTablaBD.Vars && solicitud.Accion == eAccionBD.Consulta && _tengoListasCargadas)
                    {
                        respuesta = await ConsultaVars(solicitud, respuesta);
                    }
                    else if (!_tengoListasCargadas)
                    {
                        respuesta.CodError = EnmErrorBaseDatos.Falla;
                        respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                        _logger.Warn("Aún no se encuentran cargadas las tablas necesarias, no puedo consultar {0}", solicitud?.Tabla.ToString());
                    }
                }
            }
            else
            {
                //Si es una solicitud de Evento, la vía quiere guardar un evento en la Base
                if (_envioEve != null && EnvioEventos.ConfiguracionCargada)
                    respuesta = await _envioEve.SolicitudCliente(solicitudEve, false);
                else
                {
                    respuesta.CodError = EnmErrorBaseDatos.Falla;
                    respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                }
            }

            if (respuesta == null)
            {
                respuesta = new RespuestaBaseDatos();
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            }

            if (bConsulta)
                _logger.Trace("Transcurrido FINAL: {0}/{1}/{2} TIEMPO: {3} ", solicitud.Accion, solicitud.Tabla, solicitud.Filtro, state.ElapsedWatch());
            else
                _logger.Trace("Transcurrido FINAL: {0} TIEMPO: {1} ", sEvento, state.ElapsedWatch());

            return respuesta;
        }

        /// <summary>
        /// Ejecuta el Stored Procedure directamente en la BD de la estacion
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private async Task<RespuestaBaseDatos> ConsultaUltimoTurno(SolicitudBaseDatos solicitud, bool bCompara)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            if (solicitud.Tabla == eTablaBD.GetUltimoTurno && !EnvioEventos.EventoPendiente)
            {
                respuesta = await _base.ConsultaBaseEstacion(solicitud);

                if (!string.IsNullOrEmpty(respuesta.RespuestaDB) && !bCompara)
                {
                    SolicitudBaseDatos solicitudAux = new SolicitudBaseDatos();
                    RespuestaBaseDatos respuestaAux = new RespuestaBaseDatos();
                    solicitudAux.Accion = eAccionBD.Turno;
                    solicitudAux.Tabla = eTablaBD.GetUltimoTurno;
                    solicitudAux.Filtro = respuesta.RespuestaDB;

                    respuestaAux = _turnoGst.AccionVia(solicitudAux);

                    if (respuestaAux.CodError == EnmErrorBaseDatos.SinFalla)
                    {
                        solicitudAux.Tabla = eTablaBD.UltTurnoNum;
                        respuestaAux = _turnoGst.AccionVia(solicitudAux);
                    }
                }
            }
            else if (solicitud.Tabla == eTablaBD.GetUltimoTurno && EnvioEventos.EventoPendiente)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                _logger.Info("No hago la consulta de último Turno porque hay Eventos Pendientes...");
            }
            else if (solicitud.Tabla != eTablaBD.GetUltimoTurno)
                respuesta = await _base.ConsultaBaseEstacion(solicitud);

            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Realiza consulta de Vars y ademas, evalua el estado de la numeración para informar a la via
        /// </summary>
        /// <param name="solicitud"></param>
        /// <param name="datosVars"></param>
        /// <returns></returns>
        private async Task<RespuestaBaseDatos> ConsultaVars(SolicitudBaseDatos solicitud, RespuestaBaseDatos datosVars)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            VarsBD oVars = new VarsBD();
            oVars.EstadoNumeracion = eEstadoNumeracion.NumeracionOk;

            if (_gstComandos == null)
            {
                _logger.Debug("Esperando gstcomandos");
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                return respuesta;
            }

            if (datosVars.CodError == EnmErrorBaseDatos.SinFalla)
            {
                datosVars.RespuestaDB = datosVars.RespuestaDB.Substring(1, datosVars.RespuestaDB.Length - 2);
                var json = JObject.Parse(datosVars.RespuestaDB);
                oVars.Vehiculo = (string)json.GetValue("var_veh");
            }

            //Obtiene Turno...
            RespuestaBaseDatos resAux = new RespuestaBaseDatos();
            solicitud = new SolicitudBaseDatos();
            solicitud.Accion = eAccionBD.Turno;
            solicitud.Tabla = eTablaBD.GetInfo;
            resAux = _turnoGst.AccionVia(solicitud);

            if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
            {
                _logger.Debug("Se obtiene información del turno");
                oVars.Turno = resAux.RespuestaDB;

                //Obtiene Numeradores
                solicitud.Tabla = eTablaBD.GetNumerador;
                resAux = _turnoGst.AccionVia(solicitud);

                if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
                {
                    _logger.Debug("Se obtienen los numeradores del turno");
                    oVars.Numerador = resAux.RespuestaDB;
                    TurnoBD oTurno = null;
                    oTurno = JsonConvert.DeserializeObject<TurnoBD>(oVars.Turno);

                    if (oTurno.TurnoAbierto == "N")
                    {
                        bool bEsAntiguo = false;
                        //busca el estado de esa numeracion
                        solicitud.Tabla = eTablaBD.GetEstadoNumerador;
                        resAux = _turnoGst.AccionVia(solicitud);

                        eEstadoNumeracion estadoBase;
                        bool bEnum = Enum.TryParse(resAux.RespuestaDB, out estadoBase);
                        if (bEnum)
                            _logger.Debug("Se obtiene estado de la numeración [{0}]", estadoBase.ToString());

                        //1. Obtiene fecha de ultimo evento en la bd local
                        DateTime dtUltimoEve = _envioEve.BuscarUltimoEvento();
                        double days = 0;

                        if (dtUltimoEve != DateTime.MinValue && FechaExcedeHora(dtUltimoEve))
                        {
                            //1.1 se obtienen los dias de diferencia
                            days = Math.Round(Math.Abs((dtUltimoEve - DateTime.Now).TotalDays));
                            bEsAntiguo = days >= Configuraciones.Instance.Configuracion.DiasEvento ? true : false;
                        }

                        if (!bEsAntiguo)
                        {
                            bool bParse = false;
                            //2. Obtiene fecha de ultimo transito
                            solicitud.Tabla = eTablaBD.GetFechaNumerador;
                            solicitud.Filtro = eContadores.NumeroTransito.ToString();
                            resAux = _turnoGst.AccionVia(solicitud);

                            if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
                            {
                                bParse = DateTime.TryParse(resAux.RespuestaDB, out dtUltimoEve);

                                if (bParse && (dtUltimoEve != DateTime.MinValue && FechaExcedeHora(dtUltimoEve)))
                                {
                                    //2.1 se obtienen los dias de diferencia
                                    days = Math.Abs((dtUltimoEve - DateTime.Now).TotalDays);
                                    bEsAntiguo = days >= Configuraciones.Instance.Configuracion.DiasEventoTransito ? true : false;
                                }
                            }
                            else
                                bEsAntiguo = true;

                            if (!bEsAntiguo)
                            {
                                //3. Obtiene fecha de ultimo ticket
                                solicitud.Filtro = eContadores.NumeroTicketFiscal.ToString();
                                resAux = _turnoGst.AccionVia(solicitud);

                                if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
                                {
                                    bParse = DateTime.TryParse(resAux.RespuestaDB, out dtUltimoEve);

                                    if (bParse && (dtUltimoEve != DateTime.MinValue && FechaExcedeHora(dtUltimoEve)))
                                    {
                                        //3.1 se obtienen los dias de diferencia
                                        days = Math.Abs((dtUltimoEve - DateTime.Now).TotalDays);
                                        bEsAntiguo = days >= Configuraciones.Instance.Configuracion.DiasEventoFactura ? true : false;

                                        if (bEsAntiguo && days > 0)
                                            _logger.Info("Evaluar si se debe AUTORIZAR NUMERACION -> La última factura se envío hace {0} días...", days.ToString("F0"));
                                    }
                                }
                                else
                                    bEsAntiguo = true;
                            }
                            else if (days > 0)
                                _logger.Info("Evaluar si se debe AUTORIZAR NUMERACION -> El último tránsito se envío hace {0} días...", days.ToString("F0"));
                        }
                        else if (days > 0)
                            _logger.Info("Evaluar si se debe AUTORIZAR NUMERACION -> El último evento se envío hace {0} días...", days.ToString("F0"));

                        //si los dias de diferencia exceden a los establecidos en la app config, tengo numeracion vieja, busco ult turno
                        if (bEsAntiguo)
                        {
                            //Comparo lo que se obtuvo con la numeración de la estación
                            SolicitudBaseDatos solicitudUlturno = new SolicitudBaseDatos();
                            solicitudUlturno.Accion = eAccionBD.Procedure;
                            solicitudUlturno.Tabla = eTablaBD.GetUltimoTurno;

                            RespuestaBaseDatos resUltTurno = await ConsultaUltimoTurno(solicitudUlturno, true);

                            _logger.Debug("Se obtiene información del último turno");

                            if (resUltTurno.CodError == EnmErrorBaseDatos.SinFalla)
                            {
                                UltTurno oUltTurno = null;
                                resUltTurno.RespuestaDB = resUltTurno.RespuestaDB.Replace("[", "");
                                resUltTurno.RespuestaDB = resUltTurno.RespuestaDB.Replace("]", "");
                                oUltTurno = JsonConvert.DeserializeObject<UltTurno>(resUltTurno.RespuestaDB);

                                if (oUltTurno.NumConfiable == 'S')
                                {
                                    //reviso el numero de turno, si son diferentes hay inconsistencias
                                    if (oTurno.NumeroTurno < oUltTurno.NumeroTurno)
                                    {
                                        //Ult Turno es mayor, me quedo con UltTurno
                                        oVars.EstadoNumeracion = eEstadoNumeracion.SinNumeracion;
                                        _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, "Información del último turno está desactualizada en la Base de Datos local");
                                        _logger.Info("La información del último turno está mas actualizada que la local, estado numeración: {0}", oVars.EstadoNumeracion.ToString());
                                    }
                                    else if (oTurno.NumeroTurno > oUltTurno.NumeroTurno)
                                    {
                                        oVars.EstadoNumeracion = eEstadoNumeracion.NumeracionSinConfirmar;
                                        _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, "Información del último turno varía entre la Base de datos local y el servidor de la plaza");
                                        _logger.Info("La información local está mas actualizada que la de la plaza, estado numeración: {0}", oVars.EstadoNumeracion.ToString());
                                    }
                                    else
                                    {
                                        //revisa si los numeradores de ultturno son diferentes
                                        List<NumeradorBD> lNumerador = new List<NumeradorBD>();
                                        lNumerador = JsonConvert.DeserializeObject<List<NumeradorBD>>(oVars.Numerador);
                                        eEstadoNumeracion estadoAux = oVars.EstadoNumeracion;
                                        var last = Enum.GetValues(typeof(eContadores)).Cast<eContadores>().Max();
                                        //si se borraron manualmente los numeradores hay que avisar que confirmen la numeración
                                        estadoAux = lNumerador.Count < (int)last ? eEstadoNumeracion.SinNumeracion : estadoAux;

                                        if (estadoAux != eEstadoNumeracion.SinNumeracion)
                                        {
                                            #region Numeradores UltTurno
                                            //recorro la lista para hacer la comparacion
                                            foreach (NumeradorBD nm in lNumerador)
                                            {
                                                switch (nm.TipoNumerador)
                                                {
                                                    case eContadores.NumeroTicketFiscal:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoTicketFiscal, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroFactura:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimaFactura, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroTicketNoFiscal:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoTicketNoFiscal, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.TicketAutoPaso:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoAutoPaso, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroTransito:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoTransito, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroDetraccion:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimaDetraccion, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroPagoDiferido:
                                                        if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoPagoDiferido, nm.TipoNumerador, ref estadoAux))
                                                            break;
                                                        else
                                                            continue;
                                                    case eContadores.NumeroTransitoEscape:
                                                        if (Configuraciones.Instance.Configuracion.NumeroViaEscape != 0)
                                                        {
                                                            if (NumeradorDiferente((int)nm.ValorFinal, oUltTurno.UltimoTransitoEscape, nm.TipoNumerador, ref estadoAux))
                                                                break;
                                                            else
                                                                continue;
                                                        }
                                                        else
                                                            continue;

                                                    default:
                                                        continue;
                                                }
                                                //si encuentra uno que no coincide 
                                                if (estadoAux != eEstadoNumeracion.NumeracionOk)
                                                    _logger.Info("Uno de los numeradores es diferente al de UltTurno, estado: {0}", estadoAux.ToString());
                                                break;
                                            }
                                            #endregion
                                        }
                                        else
                                        {
                                            //almaceno localmente los Numeradores faltantes
                                            _turnoGst.SetNumeradorCorrupto(true);
                                            solicitud.Tabla = eTablaBD.UltTurnoNum;
                                            solicitud.Filtro = resUltTurno.RespuestaDB;
                                            resAux = _turnoGst.AccionVia(solicitud);
                                            _turnoGst.SetNumeradorCorrupto(false);

                                            if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
                                            {
                                                //Obtiene Numeradores
                                                solicitud.Tabla = eTablaBD.GetNumerador;
                                                resAux = _turnoGst.AccionVia(solicitud);
                                            }

                                            if (resAux.CodError == EnmErrorBaseDatos.SinFalla)
                                            {
                                                //envío a lógica los numeradores de UltTurno para confirmar en pantalla
                                                _logger.Debug("Se almacenan y obtienen los numeradores correctamente");
                                                oVars.Numerador = resAux.RespuestaDB;
                                                solicitud.Tabla = eTablaBD.SetEstadoNumerador;
                                                solicitud.Filtro = eEstadoNumeracion.NumeracionSinConfirmar.ToString();
                                                _turnoGst.AccionVia(solicitud);
                                            }
                                        }

                                        if (Equals(oVars.EstadoNumeracion, estadoAux))
                                            _logger.Info("No es necesario AUTORIZAR NUMERACION, se consultó último turno y se tienen los mismo valores en la BD local...");

                                        oVars.EstadoNumeracion = estadoAux;
                                    }
                                }
                                else//la información de la estación no es confiable, regreso estado de num de la BD local
                                {
                                    oVars.EstadoNumeracion = estadoBase;
                                    if (estadoBase != eEstadoNumeracion.NumeracionOk)
                                        _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, "La información del último turno del servidor de la plaza no es confiable");
                                    _logger.Info("La numeración de la estación no es confiable, regreso estado de numeración de la BD local [{0}]", estadoBase.ToString());
                                }
                            }//se reunió la información de la numeración local, pero no se pudo consultar UltTurno
                            else
                            {
                                resAux.CodError = EnmErrorBaseDatos.Falla;
                                oVars.EstadoNumeracion = eEstadoNumeracion.NumeracionSinConfirmar;
                                _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, "No fue posible consultar información del último turno en el servidor de la plaza");
                                _logger.Info("Se consultó la BD local, pero no se pudo consultar UltTurno para comparar. estado: {0}", oVars.EstadoNumeracion.ToString());
                            }
                        }
                        //Si todo está OK pero el estado de esa numeracion era sinConfirmar, se quedará asi
                        if (estadoBase == eEstadoNumeracion.NumeracionSinConfirmar)
                        {
                            oVars.EstadoNumeracion = eEstadoNumeracion.NumeracionSinConfirmar;
                            _logger.Info("La consulta está OK pero el ultimo estado de la numeración es sin confirmar: {0}", oVars.EstadoNumeracion.ToString());
                        }
                    }//el turno estaba abierto, devuelvo la info a logica
                    else
                    {
                        List<NumeradorBD> lNumerador = new List<NumeradorBD>();
                        lNumerador = JsonConvert.DeserializeObject<List<NumeradorBD>>(oVars.Numerador);
                        var last = Enum.GetValues(typeof(eContadores)).Cast<eContadores>().Max();
                        //si se borraron manualmente o no se agregaron por un error los numeradores
                        //hay que avisar que no hay numeración para añadirlos
                        oVars.EstadoNumeracion = lNumerador.Count < (int)last ? eEstadoNumeracion.SinNumeracion : oVars.EstadoNumeracion;
                        _logger.Debug("Devuelvo info del turno abierto, estado: {0}", oVars.EstadoNumeracion.ToString());
                    }
                }//no se obtuvieron los numeradores, no hay registros
                else
                {
                    oVars.EstadoNumeracion = eEstadoNumeracion.SinNumeracion;
                    _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, "No hay registros de contadores");
                    _logger.Debug("No pude consultar los numeradores de la base local, estado: {0}", oVars.EstadoNumeracion.ToString());
                }
            }//no se obtuvo el turno, la tabla esta vacía
            else
            {
                oVars.EstadoNumeracion = eEstadoNumeracion.SinNumeracion;
                //enviar evento falla critica
                _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion,"Base de Datos de Turno inexistente");
                _logger.Debug("No existe la BD de Turnos aun, estado: {0}", oVars.EstadoNumeracion.ToString());
            }

            respuesta.CodError = resAux.CodError;
            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            respuesta.RespuestaDB = JsonConvert.SerializeObject(oVars);

            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Determina si el numerador es diferente
        /// </summary>
        /// <param name="numLocal">Valor del numerador en la BD local</param>
        /// <param name="numEst">Valor del numerador en la estación</param>
        /// <param name="num">Numerador</param>
        /// <param name="numeracion">Estado de la numeración</param>
        /// <returns>True si son diferente, de lo contrario False</returns>
        private bool NumeradorDiferente(int numLocal, int numEst, eContadores num, ref eEstadoNumeracion numeracion)
        {
            _logger.Trace("Entro...");
            bool bRet = true;

            if (numLocal > numEst)
                numeracion = eEstadoNumeracion.NumeracionSinConfirmar;
            else if (numLocal < numEst)
                numeracion = eEstadoNumeracion.SinNumeracion;
            else
                bRet = false;

            if (bRet)
            {
                string sObs = string.Format("El numerador: {0} tiene valores diferentes. Local: {1} - Plaza: {2}", num.ToString(), numLocal, numEst);
                _gstComandos.EnviarEvento(EnmFallaCritica.FCNumeracion, sObs);
                _logger.Info(sObs);
            }

            _logger.Trace("Salgo...");
            return bRet;
        }

        /// <summary>
        /// Se compara una fecha con la actual para determinar si es mayor o menor a una hora
        /// </summary>
        /// <param name="dtFecha"></param>
        /// <returns></returns>
        private bool FechaExcedeHora(DateTime dtFecha)
        {
            return Math.Round(Math.Abs((dtFecha - DateTime.Now).TotalHours)) > 1 ? true : false;
        }

        #endregion

        #region Envío de Datos

        /// ************************************************************************************************
        /// <summary>
        /// Envía data al socket del cliente.
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="data"></param>
        /// ************************************************************************************************
        private void Send(Socket handler, String data)
        {
            try
            {
                //Convierto el string a byte utilizando ASCII encoding.
                byte[] byteData = Encoding.Default.GetBytes(string.Format("{0}\n", data));

                //Comienzo a enviar la data al dispositivo remoto
                handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);
            }
            catch (Exception e)
            {
                _logger.Error("Excepción general: {0}", e.ToString());
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Envia la data cuando el cliente está listo para recibir.
        /// </summary>
        /// <param name="ar"></param>
        /// ************************************************************************************************
        private void SendCallback(IAsyncResult ar)
        {
            //Se detuvo la aplicación... ya no estoy escuchando
            if (!_estoyEscuchando)
                return;

            try
            {
                //Obtiene el socket desde el state object.
                Socket handler = (Socket)ar.AsyncState;
                //Termino de enviar la data al cliente.
                int bytesSent = handler.EndSend(ar);
            }
            catch (Exception e)
            {
                _logger.Error("Excepcion [{0}]", e.ToString());
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Envía notificacion al cliente acerca de estado de conexión y listas actualizadas
        /// </summary>
        /// <param name="sInfo"></param>
        /// ************************************************************************************************
        public void EnviaNotificacionCliente(string sInfo)
        {
            try
            {
                if( _listaSocketsAlert.Any() )
                {
                    foreach( Socket st in _listaSocketsAlert )
                    {
                        Send( st, sInfo );
                    }
                    _logger.Debug( "Envío notificación a la vía: {0}", sInfo );
                }
                else
                    _logger.Debug( "No se envió la notificación a la vía: {0}", sInfo ); 
            }
            catch (Exception e)
            {
                _logger.Error($"Excepcion, no pudo enviar notificación a la vía: {sInfo}. Detalle: {e.ToString()}");
            }
        }

        #endregion

        #region SetTimers

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer init.
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerInit()
        {
            _initTimer = new System.Timers.Timer(60000);
            _initTimer.Elapsed += OnTimedEventInit;
            _initTimer.AutoReset = true;
            _initTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer que chequea la conexión.
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerCon()
        {
            _checkConnTimer = new System.Timers.Timer();
            _checkConnTimer.Elapsed += OnTimedEventCon;
            _checkConnTimer.AutoReset = true;
            _checkConnTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer.
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerActualizaciones()
        {
            _actualizacionesTimer = new System.Timers.Timer(60000); //Cada minuto es 60000
            _actualizacionesTimer.Elapsed += OnTimedEventActualizaciones;
            _actualizacionesTimer.AutoReset = true; //false
            _actualizacionesTimer.Enabled = true;
            //actualizacionesTimer.Start();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer de Borrado
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerBorrar()
        {
            //cada hora
            _borrarTimer = new System.Timers.Timer(60 * 60 * 1000);
            _borrarTimer.Elapsed += OnTimedEventBorrar;
            _borrarTimer.AutoReset = true;
            _borrarTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del Timer de ejecución de comandos
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerComandos()
        {
            _getComandos.Accion = eAccionBD.Procedure;
            _getComandos.Tabla = eTablaBD.Comandos;
            _comandosTimer = new System.Timers.Timer(1000); //cada segundo
            _comandosTimer.Elapsed += OnTimedEventComandos;
            _comandosTimer.AutoReset = true;
            _comandosTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del Timer de ejecución de GetFechaHora
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerFechaHora()
        {
            _getFechaHora.Accion = eAccionBD.Procedure;
            _getFechaHora.Tabla = eTablaBD.GetServerDate;
            _fechaHoraTimer = new System.Timers.Timer(60000); //cada minuto
            _fechaHoraTimer.Elapsed += OnTimedEventFechaHora;
            _fechaHoraTimer.AutoReset = true;//false
            _fechaHoraTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del Timer de chequeo de tamaño de la BD
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerDataSize()
        {
            _dataSizeTimer = new System.Timers.Timer(100);
            _dataSizeTimer.Elapsed += OnTimedEventDataSize;
            _dataSizeTimer.AutoReset = false;
            _dataSizeTimer.Enabled = true;
        }

        #endregion


        #region TimerEvents

        /// ************************************************************************************************
        /// <summary>
        /// Actualiza todas las lstas por INIT.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventInit(Object source, ElapsedEventArgs e)
        {
            _logger.Trace("Entro");

            if (!_reintentandoListas)
            {
                _logger.Debug("Reintento las listas fallidas...");
                _reintentandoListas = true;

                if (_conList.NumeroListasPendientes() > 0)
                {
                    if (!_conList.Reintenta())
                    {
                        _logger.Debug("Salió mal el reintento, falta(n) {0} lista(s)...", _conList.NumeroListasPendientes());
                    }
                }
                else
                {
                    _logger.Debug("Por los momentos no hay listas pendientes por actualizar...", _conList.NumeroListasPendientes());
                    _tengoListasCargadas = true;
                }

                _reintentandoListas = false;
            }

            _logger.Trace("Salgo");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento que desencadena el chequeo de la conexión.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventCon(Object source, ElapsedEventArgs e)
        {
            _logger.Trace("Entro");

            if (!_estoyChequeandoCon || _ultimoCheckCon < DateTime.Now.AddMilliseconds(-_intervaloConn))
            {
                _estoyChequeandoCon = true;
                _ultimoCheckCon = DateTime.Now;
                eEstadoRed iTimer;
                iTimer = _checkConnection.StatusConexion();
                _statConn = iTimer;
                try
                {
                    if (iTimer == eEstadoRed.Ambas && !_initUpdate && (_modo == 1 || _modo == 3))
                    {
                        if (_initFlag)
                        {
                            bool bRetry = false;

                            if (_conList.ObtenerStoredProcedures())
                            {
                                _logger.Info("Obtenemos la lista de SP");
                                _initFlag = false;
                                _initUpdate = true;
                                _checkConnTimer.Interval = _intervaloConn;

                                IniciaTimerComandos();
                                var task = Task.Run(async () => await _conList.ActualizaPorComando(""));
                                bRetry = task.Result.Item2;

                                if (bRetry)
                                {
                                    bool aux = _tengoListasCargadas;
                                    if(!aux)
                                        _tengoListasCargadas = _conList.InicioTablaBaseDatos();
                                    //envio a logica si hay o no datos locales
                                    if(!aux)
                                        _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, _checkConnection.StatusConexion().ToString() + "|" + _tengoListasCargadas);
                                }

                                //Inicializo Timers de Actualizacion, Borrado y reintento
                                _logger.Debug("Inicializa Timers de Actualizacion, Borrado y reintento");
                                SetTimerActualizaciones();
                                //SetTimerBorrar(); GAB: comento por los momentos
                                SetTimerInit();
                            }
                            else
                            {
                                _checkConnTimer.Interval = _intervaloRetry;
                                _estoyChequeandoCon = false;
                                _logger.Error("No se pudieron cargar los Stored Procedures, sigo reintentando...");
                            }
                        }
                    }
                    else
                    {
                        if (_modo == 2 && _initFlag)
                        {
                            IniciaTimerComandos();
                            _initFlag = false;
                        }

                        if (iTimer == eEstadoRed.Ambas)
                        {
                            if (_checkConnTimer.Interval <= _intervaloRetry)
                               _checkConnTimer.Interval = _intervaloConn;
                        }
                        else
                        {
                            if (_checkConnTimer.Interval > _intervaloRetry || _checkConnTimer.Interval < _intervaloRetry)
                                _checkConnTimer.Interval = _intervaloRetry;

                            _logger.Debug("La conexión no está OK, vuelvo a comprobar conexion");
                            _logger.Debug($"No se pudo comprobar la conexión, se reintentará en {_intervaloRetry / 1000} segundos...");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _checkConnTimer.Interval = _intervaloRetry;
                    _logger.Error("Exception: {0}", ex.Message);
                }
                _estoyChequeandoCon = false;
            }
            _logger.Trace("Salgo");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Actualiza las tablas desde la Base de Datos del servidor.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventActualizaciones(Object source, ElapsedEventArgs e)
        {
            if (!_buscandoActualizaciones)
            {
                _buscandoActualizaciones = true;

                //Si desde supervisión me desconectaron, no busco actualizaciones.
                if (_gstComandos?.SupervConn != "N")
                    _conList.BuscarActualizaciones();

                _buscandoActualizaciones = false;
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Verifica si las tablas de Vars y Turno tienen exceso de registros y los borra
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventBorrar(Object source, ElapsedEventArgs e)
        {
            _conList.BorrarRegistros();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento que desencadena la ejecución del SP getComandos
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventComandos(Object source, ElapsedEventArgs e)
        {
            RespuestaBaseDatos resComando = null;

            if(_statConn >= eEstadoRed.SoloLocal)
            {
                if (!_recibiendoComandos || _dtRecibiendoComandos < DateTime.Now.AddSeconds(-60))
                {
                    _recibiendoComandos = true;
                    _dtRecibiendoComandos = DateTime.Now;

                    if (string.IsNullOrEmpty(_gstComandos?.SupervConn))
                        _gstComandos.ConsultaTablaEstado("CONSULTA");
                    
                    _getComandos.Filtro = $"'{_gstComandos.SupervConn}'";
                    var task = Task.Run(async () => await _base.ConsultaBaseEstacion(_getComandos));
                    resComando = task.Result;
                        
                    _recibiendoComandos = false;

                    if (resComando.CodError == EnmErrorBaseDatos.SinFalla && resComando.RespuestaDB != "[]")
                    {
                        _logger.Debug("Ejecuté GetComandos y encontré comandos de supervisión: {0}", resComando.RespuestaDB);
                        _gstComandos.ProcesarComando(resComando);
                    }
                }
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento que desencadena la ejecución del SP getFechaHora y evalúa si se debe iniciar o 
        /// detener el borrado de eventos
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventFechaHora(Object source, ElapsedEventArgs e)
        {
            if (!_envioFechaHora)
            {
                _envioFechaHora = true;
                RespuestaBaseDatos resFechaHora = null;

                if (_statConn >= eEstadoRed.SoloLocal && _gstComandos?.SupervConn != "N")
                {
                    DateTime dtActual = DateTime.Now;
                    var task = Task.Run(async () => await _base.ConsultaBaseEstacion(_getFechaHora));
                    resFechaHora = task.Result;

                    if (resFechaHora.CodError == EnmErrorBaseDatos.SinFalla)
                    {
                        ServerDate oServerDate = new ServerDate();
                        resFechaHora.RespuestaDB = resFechaHora.RespuestaDB.Replace("[", "");
                        resFechaHora.RespuestaDB = resFechaHora.RespuestaDB.Replace("]", "");
                        oServerDate = JsonConvert.DeserializeObject<ServerDate>(resFechaHora.RespuestaDB);

                        double seconds = Math.Abs((dtActual - oServerDate.FechaHora).TotalSeconds);

                        if (seconds > 60)
                        {
                            SolicitudEnvioEventos solicitudEve = new SolicitudEnvioEventos();

                            StringBuilder evento = new StringBuilder();
                            string sDescr = string.Format("Via No Sincronizada, Fecha del Servidor: {0} | Fecha de la vía: {1}", 
                                                            oServerDate.FechaHora.ToString("yyyy-MM-dd HH:mm:ss"),
                                                            dtActual.ToString("yyyy-MM-dd HH:mm:ss"));

                            evento.Append($"exec {CAMBIOHORARIOSP} 0,");
                            evento.AppendFormat("{0}", Configuraciones.Instance.Configuracion.Estacion);    //@nuestac
                            evento.AppendFormat(",{0}", Configuraciones.Instance.Configuracion.Via);        //@nuvia
                            evento.AppendFormat(",'{0}'", dtActual.ToString("yyyyMMdd HH:mm:ss"));        //@fecha
                            evento.AppendFormat(",'{0}'", sDescr);                                          //@descr

                            EnvioEventos.DetenerBorrado();

                            solicitudEve.AccionEvento = eAccionEventoBD.Guardar;
                            solicitudEve.Tipo = eTipoEventoBD.Evento;
                            solicitudEve.FechaGenerado = new SqlDateTime(dtActual).Value;
                            solicitudEve.SqlString = evento.ToString();
                            solicitudEve.Secuencia = 0;

                            Task.Run(async() => await _envioEve.SolicitudCliente(solicitudEve, false));

                            _logger.Debug("La hora está desincronizada");
                        }
                        else
                            EnvioEventos.IniciarBorrado();
                    }
                }
                _envioFechaHora = false;
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Verifica maximo de registros y tamaño en MB por tabla en la BD local
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventDataSize(Object source, ElapsedEventArgs e)
        {
            _dataSizeTimer.Stop();

            try
            {
                if (_dataSizeTimer.Interval == 100)
                {
                    TimeSpan tsConfig = Configuraciones.Instance.Configuracion.HoraChequeoDataBD;
                    DateTime dtAhora = DateTime.Now;
                    DateTime dtConfig = new DateTime(dtAhora.Year, dtAhora.Month, dtAhora.Day, tsConfig.Hours, tsConfig.Minutes, tsConfig.Seconds);

                    if (dtAhora.TimeOfDay != tsConfig)
                    //determinar cuanto tiempo falta para ejecutar timer
                    {
                        TimeSpan ts24 = new TimeSpan(24, 0, 0);
                        TimeSpan tsDiff = dtConfig.Subtract(dtAhora);
                        //Si el resultado es negativo, restamos al TS 24 para sacar el tiempo faltante
                        tsDiff = (tsDiff.Duration() != tsDiff) ? ts24.Subtract(tsDiff.Duration()) : tsDiff;
                        _dataSizeTimer.Interval = tsDiff.TotalMilliseconds;
                    }
                    else
                        _dataSizeTimer.Interval = MILLISECONDS;
                }
                else
                    _dataSizeTimer.Interval = MILLISECONDS;

                if (_dataSizeTimer.Interval == MILLISECONDS || _primerReporteBD)
                {
                    _logger.Debug("Hago los chequeos respectivos para ver MB y registros");
                    List<DBData> lDBData = new List<DBData>();
                    //1.Chequeo en BD Peaje
                    Utility.VerificarTablasBase(_base.Connection, true, ref lDBData);

                    //2.Chequeo en BD Eventos
                    Utility.VerificarTablasBase(_envioEve.Connection, true, ref lDBData);

                    //3.Chequeo en BD Turno
                    Utility.VerificarTablasBase(_turnoGst.Connection, true, ref lDBData);

                    if (lDBData.Count() > 0)
                    {
                        string sObservacion = "Tamaño de las BD locales - ";
                        //Envío evento de mantenimiento
                        foreach (DBData data in lDBData)
                        {
                            sObservacion += string.Format("BD[{0}] Tamaño[{1}MB]. ", data.ColTabla, data.ColTamaño);
                        }

                        GestionComandos.EnviarEventoMantenimiento(sObservacion, 'M');
                    }
                    else
                        _logger.Debug("No se pudo consultar el tamaño de las BD");

                    if (_primerReporteBD)
                        _primerReporteBD = false;
                    else
                    {
                        lDBData.Clear();

                        //1.Chequeo en BD Peaje
                        Utility.VerificarTablasBase(_base.Connection, _primerReporteBD, ref lDBData);

                        //2.Chequeo en BD Eventos
                        Utility.VerificarTablasBase(_envioEve.Connection, _primerReporteBD, ref lDBData);

                        //3.Chequeo en BD Turno
                        Utility.VerificarTablasBase(_turnoGst.Connection, _primerReporteBD, ref lDBData);

                        //Envío la información a la vía
                        if (lDBData.Count() > 0)
                        {
                            _util.EnviarNotificacion(EnmErrorBaseDatos.DBDataSize, JsonConvert.SerializeObject(lDBData));
                            _logger.Debug("Envío la información a la vía");
                        }
                    }

                    if (_primerReporteBD)
                        _logger.Info("Hay que esperar {0} {1} para disparar el Timer y revisar las BD locales",
                                _dataSizeTimer.Interval < 60000 ? (_dataSizeTimer.Interval / 1000).ToString("F") : (_dataSizeTimer.Interval / 60000).ToString("F"),
                                _dataSizeTimer.Interval < 60000 ? "segundos" : "minutos");
                }
            }
            catch(Exception ex)
            {
                _logger.Error(ex);
            }
            _dataSizeTimer.Start();
        }
        
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Chequeo e inicialización de todos los elementos necesarios para comenzar la ejecución de
        /// GetComandos.
        /// </summary>
        /// ************************************************************************************************
        private void IniciaTimerComandos()
        {
            //Reviso si existe la tabla en la BD local, de lo contrario la creo.
            _gstComandos.ConsultaTablaEstado("CHECK");
            //Busco el ultimo estado de conexion segun comando de supervisión
            //en caso de que el ultimo comando de supervision fuese DESCONECTAR si se llega a reiniciar el servicio
            //necesito saber si al iniciarlo nuevamente puedo ejecutar eventos o actualizar listas
            _gstComandos.ConsultaTablaEstado("CONSULTA");
            //Si estaba en blanco o no había registro, por Default hago update a 'S'
            if (string.IsNullOrEmpty(_gstComandos?.SupervConn))
                _gstComandos.ConsultaTablaEstado("UPDATE", "S");
            //Seteo el timer
            SetTimerComandos();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Obtiene el estado de la conexión segun comandos de supervisión
        /// </summary>
        /// <returns>Estado de la conexión</returns>
        /// ************************************************************************************************
        public static string GetSupervConn()
        {
            return _gstComandos?.SupervConn;
        }

        /// <summary>
        /// Detiene y cierra los timers
        /// </summary>
        private void DisposeTimers()
        {
            _actualizacionesTimer?.Stop();
            _actualizacionesTimer?.Dispose();
            _checkConnTimer?.Stop();
            _checkConnTimer?.Dispose();
            _initTimer?.Stop();
            _initTimer?.Dispose();
            _borrarTimer?.Stop();
            _borrarTimer?.Dispose();
            _comandosTimer?.Stop();
            _comandosTimer?.Dispose();
            _fechaHoraTimer?.Stop();
            _fechaHoraTimer?.Dispose();
            _dataSizeTimer?.Stop();
            _dataSizeTimer?.Dispose();
        }

        #region KeepAlive
        /// <summary>
        /// Arma la respuesta al comando KeepAlive
        /// </summary>
        /// <param name="sSolicitud">solicitud de KeepAlive</param>
        /// <param name="respuesta">informacion requerida por el comando KeepAlive</param>
        /// <returns>True si la solicitud es KeepAlive, de lo contrario False</returns>
        private static bool KeepAlive(string sSolicitud, ref RespuestaKAMonitor respuesta)
        {
            bool bRet = false, bEnc = false;

            try
            {
                var t = typeof(SolicitudBaseDatos).GetProperties();

                if(t.Count() > 1 && sSolicitud.Contains(t[1].Name))
                    bEnc = true;

                if (!bEnc)
                {
                    t = typeof(SolicitudEnvioEventos).GetProperties();
                    if (t.Count() > 1 && sSolicitud.Contains(t[1].Name))
                        bEnc = true;
                }

                //no es KeepAlive
                if (bEnc)
                    return bRet;

                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error
                };

                SolicitudMonitor solicitud = JsonConvert.DeserializeObject<SolicitudMonitor>(sSolicitud, settings);

                if (solicitud.Accion == eAccionMonitor.KeepAlive)
                {
                    lock (_lockRespuestaKAMonitor)
                    {
                        respuesta.UltimaRespuesta = _ultimoEstado.ToString();
                        respuesta.FechaUltimaAccion = _ultimaRespuesta;
                        respuesta.Respuestas = _respuestas;
                    }
                    bRet = true;
                }

                if(bRet && solicitud.LimpiarFallas)
                {
                    LimpiarDictionary(solicitud.MinutosConfigurados);
                }
            }
            catch (Exception)
            {
                //_logger.Error("El comando {0} no es KeepAlive", sSolicitud);
            }

            return bRet;
        }

        /// <summary>
        /// Incrementa dictionary de acuerdo a la clave
        /// </summary>
        /// <param name="key">clave para buscar en el dictionary</param>
        public static void IncrementarDictionary(EnmErrorBaseDatos key)
        {
            if(_respuestas.ContainsKey((int)key))
                _respuestas[(int)key].AumentarCantidad();

            if (key != EnmErrorBaseDatos.SinFalla)
            {
                if (_ultimoEstado == key)
                    ++_respuestas[(int)key].VecesSeguidas;
                else
                    ResetearConteoErrorSeguido();
            }

            _ultimaRespuesta = DateTime.Now;
            _ultimoEstado = key;
        }

        /// <summary>
        /// Agrega valores iniciales al diccionario
        /// </summary>
        private static void LlenarDictionary()
        {
            try
            {
                if (!_respuestas.Any())
                {
                    var last = Enum.GetValues(typeof(eKABaseDatos)).Cast<eKABaseDatos>().Max();
                    for (int i = 0; i <= (int)last; i++)
                    {
                        if (ConsiderarRespuesta(i))
                        {
                            ErrorKeepAlive oErrorKA = new ErrorKeepAlive();
                            oErrorKA.Error = Enum.GetName(typeof(eKABaseDatos), i);
                            _respuestas.Add(i, oErrorKA);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                _logger.Error("Excepción general: {0}", e.ToString());
            }
        }

        /// <summary>
        /// Limpia el valor de las respuestas almacenadas en el dictionary, despues de recibir un comando KeepAlive
        /// </summary>
        private static void LimpiarDictionary(int nMinutos)
        {
            try
            {
                nMinutos = nMinutos * -1;
                DateTime dtActual = DateTime.Now;
                DateTime dtAntes = dtActual.AddMinutes(nMinutos);

                //revisa primero si algun elemento de la lista es antiguo
                if (_respuestas.Any(x => x.Value.Fecha.Any(f => f.TimeOfDay.TotalMinutes <= dtAntes.TimeOfDay.TotalMinutes)))
                {
                    foreach (int key in _respuestas.Keys.ToList())
                    {
                        //se busca en la lista el último index el cual sea la falla mas antigua a partir de los minutos proporcionados
                        int result = _respuestas[key].Fecha.FindLastIndex(t => t.TimeOfDay.TotalMinutes <= dtAntes.TimeOfDay.TotalMinutes);
                        if (result >= 0)
                        {
                            ++result;
                            //se sacan de la lista todas las fechas anteriores a este index
                            _respuestas[key].Fecha.RemoveRange(0, result);
                            //se descuentan la cantidad de fallas
                            _respuestas[key].Cantidad = _respuestas[key].Fecha.Count;
                            //si habian fallas seguidas, se reemplaza ese numero con la cantidad total de fallas (si las veces seguidas eran mas)
                            if (_respuestas[key].VecesSeguidas > _respuestas[key].Cantidad)
                                _respuestas[key].VecesSeguidas = _respuestas[key].Cantidad;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                _logger.Error("Excepción general: {0}", e.ToString());
            }
        }

        /// <summary>
        /// Evalúa si se debe sumar la respuesta para considerarla en comando KeepAlive
        /// </summary>
        /// <param name="nRes"></param>
        /// <returns>True si hay que sumarla, de lo contrario False</returns>
        private static bool ConsiderarRespuesta(int nRes)
        {
            bool bRet = false;

            try
            {
                bRet = Enum.IsDefined(typeof(eKABaseDatos), nRes);

                if (bRet && (_statConn == eEstadoRed.SoloLocal || _statConn == eEstadoRed.Ninguna))
                {
                    if ((eKABaseDatos)nRes >= eKABaseDatos.ErrorBDLogin)
                        bRet = false;
                }

            }
            catch (Exception e)
            {
                _logger.Error("KA Exception -> {0}", e.ToString());
            }

            return bRet;
        }

        /// <summary>
        /// Limpia el valor de las veces seguidas de un error, si el siguiente es diferente
        /// </summary>
        private static void ResetearConteoErrorSeguido()
        {
            try
            {
                foreach (int key in _respuestas.Keys.ToList())
                    _respuestas[key].VecesSeguidas = 0;
            }
            catch (Exception e)
            {
                _logger.Error("Excepción general: {0}", e.ToString());
            }
        }
        #endregion
    }
}
