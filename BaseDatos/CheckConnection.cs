using Entidades;
using ModuloBaseDatos.Entidades;
using System;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Utiles;

namespace ModuloBaseDatos
{
    public class CheckConnection
    {
        #region Propiedades
        public static eEstadoRed UltimoStatConexion { get { return _ultimoEstado; } set { _ultimoEstado = value; } }
        #endregion

        #region Variables de la clase
        private Utility _util = null;
        private string _connectionString;
        private static string _msg = "", _sConnEst = "";
        private static eEstadoRed _ultimoEstado;
        private static bool _existeLocal = false;
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        #endregion

        public enum eBaseDatos { Nada, CrearBaseDatos, QueryLocal, QueryEstacion }

        #region Construccion
        /// ************************************************************************************************
        /// <summary>
        /// Constructor. Establece la string de conexión a la Base de Datos.
        /// </summary>
        /// <param name="config">La configuración del archivo App.config</param>
        /// ************************************************************************************************
        public CheckConnection()
        {
            _logger.Trace("Entro...");

            //Obtiene configuracion
            _connectionString = Configuraciones.Instance.Configuracion.LocalConnection;
            _util = new Utility();

            if (!_existeLocal)
                CheckDBLocal();

            _logger.Trace("Salgo...");
        }

        public void Init()
        {
            CheckLinkedServer();
        }
        #endregion

        #region Linked Server
        /// ************************************************************************************************
        /// <summary>
        /// Verifica si el Servidor proporcionado se encuentra vinculado en la Base de Datos Local.
        /// Si no está vinculado, intenta vincularlo e informa si tuvo éxito o no.
        /// </summary>
        /// <returns>True si pudo verficar que el servidor está vinculado, de lo contrario false</returns>
        /// ************************************************************************************************
        private bool CheckLinkedServer()
        {
            string sCheckLinked, sQueryLinked, sRemoteLogin;
            bool bLinkedServer = false;

            //Query para revisar si en la tabla sys.servers se encuentra el servidor ServidorPath (especificado en App.config)
            sQueryLinked = $"select * from sys.servers where name = N'{Configuraciones.Instance.Configuracion.ServidorPath}'";
            //Query que testea la conexión al linked server ServidorPath (especificado en App.config)
            sCheckLinked = $"sp_testlinkedserver [{Configuraciones.Instance.Configuracion.ServidorPath}]";
            //Query para revisar el log que se utiliza para conectarse al servidor de la estación, contra el UID que se tiene localmente
            sRemoteLogin = $"sp_helplinkedsrvlogin [{Configuraciones.Instance.Configuracion.ServidorPath}]";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sQueryLinked, connection))
            {
                try
                {
                    command.CommandTimeout = 2;
                    //Establezco la conexión
                    connection.Open();
                    //Primero revisa si existe en sys.servers
                    SqlDataReader reader = command.ExecuteReader();
                    if (!reader.HasRows) //No existe, intenta vincular el servidor...
                    {
                        reader.Close();
                        //Hay que agregar y configurar el linked server
                        string sConsulta;
                        string sPasswordDB = Utility.GetHashString(Configuraciones.Instance.Configuracion.UID.ToUpper());

                        sConsulta = $@"
                        USE [master] 
                        EXEC master.dbo.sp_addlinkedserver @server = N'{Configuraciones.Instance.Configuracion.ServidorPath}', @srvproduct=N'SQL Server' 
                        EXEC master.dbo.sp_addlinkedsrvlogin @rmtsrvname=N'{Configuraciones.Instance.Configuracion.ServidorPath}',@useself=N'False',@locallogin=NULL,@rmtuser=N'{Configuraciones.Instance.Configuracion.UID}',@rmtpassword='{sPasswordDB}' 
                        EXEC master.dbo.sp_serveroption @server = N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname = N'collation compatible', @optvalue = N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'data access', @optvalue=N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'dist', @optvalue=N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'pub', @optvalue=N'false' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'rpc', @optvalue=N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'rpc out', @optvalue=N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'sub', @optvalue=N'false' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'connect timeout', @optvalue=N'0' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'collation name', @optvalue=null 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'lazy schema validation', @optvalue=N'false' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'query timeout', @optvalue=N'0' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'use remote collation', @optvalue=N'true' 
                        EXEC master.dbo.sp_serveroption @server=N'{Configuraciones.Instance.Configuracion.ServidorPath}', @optname=N'remote proc transaction promotion', @optvalue=N'false' 
                        USE [{ Configuraciones.Instance.Configuracion.DatabaseName}]";

                        try
                        {
                            command.CommandText = sConsulta;
                            command.ExecuteNonQuery();
                            _logger.Info($"Se agregó el servidor vinculado {Configuraciones.Instance.Configuracion.ServidorPath}");
                        }
                        catch (SqlException e)
                        {
                            _logger.Error($"Excepción al agregar el servidor vinculado {e.Number}:{e.Message}");
                        }
                    }
                    else
                    {
                        _logger.Info($"El servidor {Configuraciones.Instance.Configuracion.ServidorPath} está vinculado, comienza el test de Remote Login...");
                        reader.Close();
                    }

                    //Se comprueba que el Remote Login sea igual al UID obtenido del numero de via y estacion que envió lógica...
                    try
                    {
                        string sLogin = "";
                        command.CommandText = sRemoteLogin;
                        reader = command.ExecuteReader();

                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                sLogin = reader["Remote Login"].ToString();
                            }
                        }
                        reader.Close();

                        if (Configuraciones.Instance.Configuracion.UID != sLogin)
                        {
                            _logger.Debug($"El RemoteLogin: {sLogin} del servidor vinculado: {Configuraciones.Instance.Configuracion.ServidorPath} es distinto al UID obtenido de lógica: {Configuraciones.Instance.Configuracion.UID}, se cambiará el login...");
                            UpdateLinkedServerLogin();
                        }
                        _logger.Debug($"El servidor {Configuraciones.Instance.Configuracion.ServidorPath} tiene asignado el RemoteLogin, comienza el test de conexion...");
                    }
                    catch (SqlException e)
                    {
                        _logger.Error($"Excepción al obtener el Remote Login del servidor vinculado {e.Number}:{e.Message}");
                    }

                    //Se comprueba la conexión del linked server, esto es en caso de que se haya añadido uno incorrecto
                    try
                    {
                        command.CommandText = sCheckLinked;
                        command.ExecuteNonQuery();
                        _logger.Info($"El servidor {Configuraciones.Instance.Configuracion.ServidorPath} se encuentra activo.");
                        bLinkedServer = true;
                    }
                    catch (SqlException e)
                    {
                        _logger.Error($"Excepción al testear conexión: {e.Number}:{e.Message}");
                        _logger.Error($@"El servidor especificado: {Configuraciones.Instance.Configuracion.ServidorPath} es incorrecto, por favor compruebe 
                                    que el valor especificado en la configuración PLAZA_INSTANCE_NAME sea 
                                    correcto, de lo contario modifique y reinicie el servicio..");
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error($"SQL Excepcion {e.Number}:{e.Message}");
                }
                catch (Exception e)
                {
                    _logger.Error($"Otras Excepciones {e.Message}");
                }
            }
            return bLinkedServer;
        }

        /// <summary>
        /// Actualiza el Remote Login del Linked Server
        /// </summary>
        /// <returns>True si lo actualizó, de lo contrario False</returns>
        public bool UpdateLinkedServerLogin()
        {
            string sQueryLinked = "";
            bool bCambioPass = false;

            //Query para revisar si en la tabla sys.servers se encuentra el servidor ServidorPath (especificado en App.config)
            try
            {
                sQueryLinked = $"USE [master] exec sp_addlinkedsrvlogin @rmtsrvname ='{Configuraciones.Instance.Configuracion.ServidorPath}', @useself = 'FALSE', " +
                               $"@locallogin = NULL, @rmtuser = '{Configuraciones.Instance.Configuracion.UID}', @rmtpassword = '{Utility.GetHashString(Configuraciones.Instance.Configuracion.UID.ToUpper())}'";
            }
            catch(Exception e)
            {
                _logger.Error(e.ToString());
                return bCambioPass;
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand(sQueryLinked, connection))
            {
                try
                {
                    command.CommandTimeout = 2;
                    //Establezco la conexión
                    connection.Open();
                    command.ExecuteNonQuery();
                    bCambioPass = true;
                    _logger.Debug($"Se cambió la contraseña del servidor vinculado {Configuraciones.Instance.Configuracion.ServidorPath}");
                }
                catch (SqlException e)
                {
                    _logger.Error($"SQL Excepcion {e.Number}:{e.Message}");
                    ErrorConexion(15015, eBaseDatos.Nada);
                }
                catch (Exception e)
                {
                    _logger.Error($"Otras Excepciones {e.ToString()}");
                }
            }
            return bCambioPass;
        }
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Verifica si existe la BD local establecida en la App.config, de lo contrario la crea.
        /// </summary>
        /// ************************************************************************************************
        private void CheckDBLocal()
        {
            _logger.Trace("Entro...");
            string sConn,sQuery;
            bool bStatus;
            sConn = "Server=" + Configuraciones.Instance.Configuracion.LocalPath + ";Database=master;User Id=sa;Password=TeleAdmin01;Connection Timeout=15";

            //qry para evaluar si existe la Bd local, si existe devuelve 2 de lo contrario 1
            sQuery = $"IF DB_ID('{Configuraciones.Instance.Configuracion.DatabaseName}') IS NULL BEGIN CREATE DATABASE [{Configuraciones.Instance.Configuracion.DatabaseName}] PRINT 1 END ELSE BEGIN PRINT 2 END";
            bStatus = ConsultaBase(sConn, sQuery, eBaseDatos.CrearBaseDatos);

            if (bStatus)
            {
                _existeLocal = true;

                if (_msg == "1")
                    _logger.Info($"Se ha creado la Base de Datos {Configuraciones.Instance.Configuracion.DatabaseName} correctamente.");
                else if (_msg == "2")
                    _logger.Info($"Se confirma la existencia de la Base de Datos {Configuraciones.Instance.Configuracion.DatabaseName}.");
            }

            sQuery = $"IF OBJECT_ID('AutPas_Utilizadas','U') IS NULL BEGIN CREATE TABLE AutPas_Utilizadas (ape_codig varchar(25), ape_feven datetime); PRINT 1 END ELSE BEGIN PRINT 2 END";
            bStatus = ConsultaBase( _connectionString, sQuery, eBaseDatos.CrearBaseDatos );

            if( bStatus )
            {
                if( _msg == "1" )
                    _logger.Info( $"Se ha creado la tabla AutPas_Utilizadas correctamente." );
                else if( _msg == "2" )
                    _logger.Info( $"Se confirma la existencia de la tabla AutPas_Utilizadas." );
            }
            else
            {
                _existeLocal = false;
            }

            //clear
            if(_existeLocal)
                SqlConnection.ClearPool(new SqlConnection(sConn));

            _logger.Trace("Salgo...");
        }

        #region Errores 

        /// ************************************************************************************************
        /// <summary>
        /// Informa el error encontrado al intentar comprobar la conexión a la BD local o a la estación.
        /// En caso de que suceda un error por no tener vinculado el servidor, desde esta función se 
        /// establecerá el vínculo y se informará si tuvo éxito o no.
        /// </summary>
        /// <param name="iNumer">El número del error a informar</param>
        /// <returns>True si se comprobó el linked server, de lo contrario false.</returns>
        /// ************************************************************************************************
        private bool ErrorConexion(int iNumer, eBaseDatos enumBase)
        {
            _logger.Trace("Entro...");
            bool bRet = false;
            string msj = "";
            switch (iNumer)
            {
                case -2:
                    {
                        msj = "No se pudo comprobar la conexión debido a TIMEOUT, la instancia SQL no respondió a tiempo.";
                        _logger.Error(msj);
                        break;
                    }
                case -1:
                case 2:
                    {
                        if (enumBase == eBaseDatos.QueryLocal)
                        {
                            msj = $"Error al intentar una conexión a {Configuraciones.Instance.Configuracion.LocalPath}. Por favor, revise " +
                                        "si los valores son correctos, de lo contario modifique y reinicie el servicio. " +
                                        "Si los valores son correctos verifique si el servicio de la instancia SQL está en ejecución.";
                        }
                        else if(enumBase == eBaseDatos.QueryEstacion)
                        {
                            msj = $"Error al intentar una conexión a {Configuraciones.Instance.Configuracion.ServidorPath}. Por favor verifique si este se encuentra activo" +
                                      ", si el cable de red está desconectado o si el adaptador red está deshabilitado";
                        }

                        _logger.Error(msj);
                        break;
                    }
                case 1225:
                    {
                        msj = "Error al intentar una conexión al LOCAL_INSTANCE_NAME especificado. Por favor, revise " +
                                        "si los valores son correctos, de lo contario modifique y reinicie el servicio. " +
                                        "Si los valores son correctos verifique si el servicio de la instancia SQL está en ejecución.";
                        _logger.Error(msj);
                        break;
                    }
                case 4060:
                    {
                        msj = "Error al buscar la Base de Datos especificada en LOCAL_INSTANCE_DB_NAME. Por favor, revise " +
                                        "si el nombre es correcto, de lo contario modifique y reinicie el servicio.";
                        _logger.Error(msj);
                        break;
                    }
                    //Caso en el cual se intente hacer un query en la estación desde la Bd local y falle.
                case 7202:
                case 15015:
                    {
                        msj = "Error al buscar el PLAZA_INSTANCE_NAME especificado, se intentará " +
                                        "añadir como Linked Server...";
                        _logger.Error(msj);

                        if (CheckLinkedServer())
                        {
                            msj = "Servidor " + Configuraciones.Instance.Configuracion.ServidorPath + " ha sido vinculado correctamente.";
                            _logger.Info(msj);
                            bRet = true;
                        }
                        else
                        {
                            msj = $"Error al vincular el servidor: {Configuraciones.Instance.Configuracion.ServidorPath} especificado en PLAZA_INSTANCE_NAME. Por favor, revise " +
                                        "si los valores son correctos, de lo contario modifique y reinicie el servicio.";
                            _logger.Error(msj);
                        }
                        break;
                    }
                case 7314:
                    {
                        if (UpdateLinkedServerLogin())
                        {
                            bRet = true;
                            msj = "Contraseña modificada correctamente para el servidor: " + Configuraciones.Instance.Configuracion.ServidorPath;
                            _logger.Info(msj);
                        }
                        else
                        {
                            msj = "Error al buscar la Base de Datos especificada en PLAZA_INSTANCE_DB_NAME. Por favor, revise " +
                                            "si el nombre es correcto, de lo contario modifique y reinicie el servicio.";
                            _logger.Error(msj);
                        }
                        break;
                    }
            }
            _logger.Trace("Salgo...");
            return bRet;
        }

        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Comprueba el estado de la conexión a la BD local y a la estación.
        /// </summary>
        /// <returns>El Status de la conexión
        /// 0 = No hay conexión ni a la BD local ni a la principal
        /// 1 = Solo la BD local está OK
        /// 2 = Sola la BD estacion está OK
        /// 3 = La BD local y principal están OK
        /// </returns>
        /// ************************************************************************************************
        public eEstadoRed StatusConexion()
        {
            _logger.Trace("Entro...");
            string sQuery,sDebug = "";
            bool bStatusLocal, bStatusEst;
            eEstadoRed estado = eEstadoRed.Ambas;

            //Si al inicio del servicio no se pudo crear la BD local, me aseguro de que se cree antes de continuar.
            if (!_existeLocal)
                CheckDBLocal();

            //Compruebo conexión a la BD local:
            sQuery = $"USE [{Configuraciones.Instance.Configuracion.DatabaseName}] SELECT 1";
            bStatusLocal = ConsultaBase(_connectionString, sQuery, eBaseDatos.QueryLocal);

            //Compruebo conexión a la BD de la estación:
            sQuery = $"USE [{Configuraciones.Instance.Configuracion.ServidorBd}] SELECT 1";
            if (string.IsNullOrEmpty(_sConnEst))
            {
                string sPasswordDB = Utility.GetHashString(Configuraciones.Instance.Configuracion.UID.ToUpper());
                _sConnEst = $"Server={Configuraciones.Instance.Configuracion.ServidorPath};Database={Configuraciones.Instance.Configuracion.ServidorBd};User Id={Configuraciones.Instance.Configuracion.UID};Password={sPasswordDB};Connection Timeout=1";
            }
            bStatusEst = ConsultaBase(_sConnEst, sQuery, eBaseDatos.QueryEstacion);
            
            if (!bStatusLocal && (!bStatusEst || AsynchronousSocketListener.GetSupervConn() == "N"))
            {
                estado = eEstadoRed.Ninguna;
                if (bStatusEst)
                    sDebug = $"No se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName}. Y la via está DESCONECTADA (Comando CG)";
                else
                    sDebug = $"No se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName}. Ni la conexión al servidor: {Configuraciones.Instance.Configuracion.ServidorPath}";
                EnvioEventos.Conexion = false;
            }
            else if (bStatusLocal && (!bStatusEst || AsynchronousSocketListener.GetSupervConn() == "N"))
            {
                estado = eEstadoRed.SoloLocal;
                if(bStatusEst)
                    sDebug = $"Se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName}. Pero la via está DESCONECTADA (Comando CG)";
                else
                    sDebug = $"Se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName}. Pero no se comprobó la conexión al servidor: {Configuraciones.Instance.Configuracion.ServidorPath}";
                EnvioEventos.Conexion = false;
            }
            else if (!bStatusLocal && bStatusEst)
            {
                estado = eEstadoRed.SoloServidor;
                sDebug = $"Se comprobó la conexión al servidor: {Configuraciones.Instance.Configuracion.ServidorPath}. Pero no se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName}";
                //Consulto si habia una desconexion anterior
                EnvioEventos.Conexion = AsynchronousSocketListener.GetSupervConn() == "S" ? true : false;
            }
            else if (bStatusLocal && bStatusEst)
            {
                estado = eEstadoRed.Ambas;
                sDebug = $"Se comprobó la conexión a la Base de Datos Local: {Configuraciones.Instance.Configuracion.DatabaseName} y también la conexión al servidor: {Configuraciones.Instance.Configuracion.ServidorPath}";
                //Consulto si habia una desconexion anterior
                EnvioEventos.Conexion = AsynchronousSocketListener.GetSupervConn() == "S" ? true : false;
            }

            //Si hubo algún cambio en el estado de la conexión envío la notificación.
            if(_ultimoEstado != estado)
            {
                _logger.Info(sDebug);
                _ultimoEstado = estado;
                _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, estado.ToString());
            }
            
            _logger.Trace("Salgo...");
            return estado;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Realiza una conexión a la BD deseada y ejecuta una consulta para comprobar el estado de la BD.
        /// </summary>
        /// <param name="sConnectionString">String para realizar la conexion</param>
        /// <param name="sConsulta">Query a ejecutar en dicha conexión</param>
        /// <returns>True si pudo ejecutar el query, de lo contario false.</returns>
        /// ************************************************************************************************
        private bool ConsultaBase(string sConnectionString, string sConsulta, eBaseDatos enumBase)
        {
            _logger.Trace("Entro...");
            string sResult;
            bool bStatus = false;
            using (SqlConnection connection = new SqlConnection(sConnectionString))
            using (SqlCommand command = new SqlCommand(sConsulta, connection))
            {
                try
                {
                    //Establezco la conexión
                    connection.Open();
                    command.CommandTimeout = 2;

                    if (enumBase == eBaseDatos.CrearBaseDatos)
                    {
                        command.Connection.InfoMessage += new SqlInfoMessageEventHandler(InfoMessage);
                        try
                        {
                            command.CommandTimeout = 5;
                            command.ExecuteNonQuery();
                            bStatus = true;
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion BASE. {0}:{1}", e.Number, e.Message);
                        }
                    }
                    else
                    {
                        //Ejecuta la consulta
                        sResult = command.ExecuteScalar().ToString();
                        if (sResult != null)
                            bStatus = true;
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error($"SQL Excepcion [{e.Number}:{e.Message}]");
                    bStatus = ErrorConexion(e.Number, enumBase);
                }
                catch (Exception e)
                {
                    _logger.Error($"Otras Excepciones. {e.ToString()}");
                }
            }
            _logger.Trace("Salgo...");
            return bStatus;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Recupera el mensaje que la Base de Datos imprimió con "PRINT" y lo almacena 
        /// para ciertas validaciones.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private static void InfoMessage(object sender, SqlInfoMessageEventArgs e)
        {
            _msg = e.Message;
        }

    }
}