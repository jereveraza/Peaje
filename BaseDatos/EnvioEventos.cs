using Entidades;
using Entidades.ComunicacionBaseDatos;
using ModuloBaseDatos.Entidades;
using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace ModuloBaseDatos
{
    /// ************************************************************************************************
    /// <summary>
    /// Clase que funciona como apoyo para la gestion de eventos.
    /// </summary>
    /// ************************************************************************************************
    public class Evento
    {
        public int Numero { get; set; }
        public string SqlString { get; set; }
        public eEstado Estado { get; set; }
        public int Intentos { get; set; }
        public DateTime Fecha { get; set; }
    }

    public class EnvioEventos
    {
        #region GetSet
        public static bool Conexion { get; set; }
        public static bool EventoPendiente { get; set; }
        public static bool ConfiguracionCargada { get; set; }
        public string Connection { get { return _connectionString; } set { _connectionString = value; } }
        #endregion

        #region memberVars
        private static string _bdEve, _msg, _connectionStringEst, _connectionString, _tbEve, _tbOnl, _evePath, _tbEveMin, _tbOnlMin, _viaEst, _viaEscEst;
        private static Logger _elog = LogManager.GetLogger("elog");
        private static Logger _elogReceived = LogManager.GetLogger("elogReceived");
        private static Logger _elogSended = LogManager.GetLogger("elogSended");
        private static Logger _elogErrorSended = LogManager.GetLogger("elogErrorSended");
        //private static Logger _elogCounter = LogManager.GetLogger("elogCounter");
        private static ConfiguracionBaseDatos _con = null;
        private static System.Timers.Timer _aTimerPrioridad, _aTimerSinPrioridad, _aTimerPrioridadMedia, _deleteEventsTimer, _iniTimer, _recuperaEve;
        private static ManualResetEvent _waitConfig = new ManualResetEvent(false);
        private static int _retries, _eveDay, _failedDay;
        public static IBaseDatos _param;
        private static EnvioEventos _eve = new EnvioEventos();
        private static bool _estaBorrando = false, _bOnlineReg = false;
        private static Utility _util;
        #endregion

        #region Constantes
        private const string ERRORSQLSP = "ViaNet.usp_setErrorSQL";
        private const double MILLISECONDS = 3600 * 24000;
        #endregion

        //static PerformanceCounter[] PerfCounters = new PerformanceCounter[10];

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa y configura lo necesario para comenzar el envio de eventos
        /// </summary>
        /// <param name="config"></param>
        /// ************************************************************************************************
        public static void Init(ConfiguracionBaseDatos config)
        {
            int nEve;
            bool bTry, bEve;
            string sLogPath;
            _util = new Utility();

            #region ConfigEstacion
            ConfiguracionCargada = false;
            _con = config;
            //Generación de Password para realizar conexión al servidor de la estación:
            string sPasswordDB = Utility.GetHashString(_con.UID.ToUpper());
            //Connection string al servidor de la estación:
            _connectionStringEst = $"Server={_con.ServidorPath};Database={_con.ServidorBd};User Id={_con.UID};Password={sPasswordDB};Connection Timeout=1";
            #endregion

            #region ConfigLocal
            //Nombre BD para almacenar eventos
            _bdEve = config.EventDb;
            //Directorio donde se generaran los eventos que no se pudieron guardar en la BD local
            _evePath = config.EventDir;
            //Connection string a la Base de Datos local:
            _connectionString = "Server=" + _con.LocalPath + ";Database=" + _bdEve +
                                    ";User Id=sa;Password=TeleAdmin01;Connection Timeout=2;Max Pool Size=200";

            //Se obtienen localmente los valores de reintentos, dias permanencia evento, dias permanencia online:
            bTry = Int32.TryParse(_con.Retries, out _retries);
            bEve = Int32.TryParse(_con.EventoPermanencia, out nEve);

            //Default si no esta la config:
            _eveDay = bEve ? nEve : 365;

            _failedDay = _con.MaxDiasEventosFallidos;

            //Nombre de las tablas a utilizar para almacenar eventos generados por la aplicación:
            _tbEve = "Evento";
            _tbOnl = "Online";
            _tbEveMin = _tbEve.Substring(0, 3).ToLower();
            _tbOnlMin = _tbOnl.Substring(0, 3).ToLower();

            //Directorio donde se almacenarán los Logs del Envio de Eventos
            sLogPath = _con.LogEveDir;
            //Asignación de directorio para almacenar los logs:
            LogManager.Configuration.Variables["eveLog"] = sLogPath;
            
            //En Try-Catch porque si no hay acceso a la ruta especificada da error y no avanza el inicio.
            try
            {
                //Verifico si existe el dir para guardar el log, de lo contrario lo crea...
                if (!Directory.Exists(sLogPath))
                    Directory.CreateDirectory(sLogPath);
                //Verifico si existe el dir para guardar eventos, de lo contrario lo crea...
                if (!Directory.Exists(_evePath))
                    Directory.CreateDirectory(_evePath);
            }
            catch(Exception e)
            {
                _elog.Error("Error en el directorio especificado para los Eventos. [{0}]",e.ToString());
            }

            _viaEst = config.Via.ToString("D3") + config.Estacion.ToString("D2");
            if(config.NumeroViaEscape != 0)
                _viaEscEst = config.NumeroViaEscape.ToString("D3") + config.Estacion.ToString("D2");
            #endregion

            EventoPendiente = false;
            ConfiguracionCargada = true;

            //SetUpPerformanceCounters();

            _elog.Info("Configuración de Envio de Eventos cargada");
            _waitConfig.Set(); //Ya tengo la configuracion..
        }

        #region counters
        /*private enum ADO_Net_Performance_Counters
        {
            NumberOfActiveConnectionPools,
            NumberOfReclaimedConnections,
            //HardConnectsPerSecond,
            //HardDisconnectsPerSecond,
            NumberOfActiveConnectionPoolGroups,
            NumberOfInactiveConnectionPoolGroups,
            //NumberOfInactiveConnectionPools,
            //NumberOfNonPooledConnections,
            NumberOfPooledConnections,
            NumberOfStasisConnections,
            // The following performance counters are more expensive to track.
            // Enable ConnectionPoolPerformanceCounterDetail in your config file.
                 //SoftConnectsPerSecond,
                 //SoftDisconnectsPerSecond,
                 NumberOfActiveConnections,
                 NumberOfFreeConnections
        }

        private static void SetUpPerformanceCounters()
        {
            PerfCounters = new PerformanceCounter[8];
            string instanceName = GetInstanceName();
            Type apc = typeof(ADO_Net_Performance_Counters);
            int i = 0;
            foreach (string s in Enum.GetNames(apc))
            {
                PerfCounters[i] = new PerformanceCounter();
                PerfCounters[i].CategoryName = ".NET Data Provider for SqlServer";
                PerfCounters[i].CounterName = s;
                PerfCounters[i].InstanceName = instanceName;
                i++;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int GetCurrentProcessId();

        private static string GetInstanceName()
        {
            //This works for Winforms apps.
            string instanceName =
                System.Reflection.Assembly.GetEntryAssembly().GetName().Name;

            // Must replace special characters like (, ), #, /, \\
            //instanceName = AppDomain.CurrentDomain.FriendlyName.ToString().Replace('(', '[')
              //      .Replace(')',']').Replace('#','_').Replace('/','_').Replace('\\','_');

            // For ASP.NET applications your instanceName will be your CurrentDomain's
            // FriendlyName. Replace the line above that sets the instanceName with this:
            // instanceName = AppDomain.CurrentDomain.FriendlyName.ToString().Replace('(','[')
            // .Replace(')',']').Replace('#','_').Replace('/','_').Replace('\\','_');

            string pid = GetCurrentProcessId().ToString();
            instanceName = instanceName + "[" + pid + "]";
            _elogCounter.Debug("Instance Name: {0}", instanceName);
            return instanceName;
        }

        private static void WritePerformanceCounters()
        {
            _elogCounter.Debug("---------------------------");
            foreach (PerformanceCounter p in PerfCounters)
            {
                if(p != null)
                    _elogCounter.Debug("{0} = {1}", p.CounterName, p.NextValue());
            }
            _elogCounter.Debug("---------------------------");
        }*/
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Comienza el Timer de inicio del envio de eventos
        /// </summary>
        /// ************************************************************************************************
        public void Start()
        {
            _waitConfig.WaitOne(); //Espero a que me envíen la configuración
            _elog.Trace("Entro");
            SetTimerStart();
            _elog.Trace("Salgo");
        }

        #region Timers y Eventos

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer que chequea si existe la BD de eventos y las tablas.
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerStart()
        {
            _iniTimer = new System.Timers.Timer();
            _iniTimer.Elapsed += OnTimedEventStart;
            _iniTimer.AutoReset = false;
            _iniTimer.Start();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer que comienza a ejecutar los eventos
        /// PENDIENTES
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerEventExec()
        {
            // Create a timer with a second interval.
            _aTimerPrioridad = new System.Timers.Timer(1000);
            // Hook up the Elapsed event for the timer. 
            _aTimerPrioridad.Elapsed += OnTimedEvent;
            _aTimerPrioridad.AutoReset = false;
            _aTimerPrioridad.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer que verifica si hay eventos por eliminar por antiguedad
        /// en la Base de Datos local
        /// </summary>
        /// ************************************************************************************************
        private static void SetTimerEventDelete()
        {
            _deleteEventsTimer = new System.Timers.Timer(100);
            // Hook up the Elapsed event for the timer. 
            _deleteEventsTimer.Elapsed += OldEventDelete;
            _deleteEventsTimer.AutoReset = false;
            _deleteEventsTimer.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicializa los parámetros del timer que verifica si hay eventos recuperar
        /// en disco que tengan cierto tiempo de antiguedad (definido en el .config)
        /// </summary>
        /// ************************************************************************************************
        private void SetTimerRecuperaEve()
        {
            _recuperaEve = new System.Timers.Timer(_con.IntervaloRecuperaEventos * 60000);
            // Hook up the Elapsed event for the timer. 
            _recuperaEve.Elapsed += OnRecuperaEvent;
            _recuperaEve.AutoReset = false;
            _recuperaEve.Enabled = true;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento desencadenado por el timer de Inicio, verifica que se tengan los elementos necesarios 
        /// para arrancar el envío de eventos
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEventStart(Object source, ElapsedEventArgs e)
        {
            _elog.Trace("Entro");
            _iniTimer.Stop();
            string sConn;
            bool bResult = false;
            SqlCommand command = new SqlCommand();

            sConn = "Server=" + _con.LocalPath + ";Database=master;User Id=sa;Password=TeleAdmin01;Connection Timeout=1";

            //1ero Chequeo si la BD de eventos especificada en config existe, de lo contrario la crea.
            command.CommandText = "IF DB_ID('@base') IS NULL BEGIN CREATE DATABASE [@base] PRINT 1 END ELSE BEGIN PRINT 2 END";
            command.CommandText = command.CommandText.Replace("@base", _bdEve);

            var task = EjecutaConsulta(sConn, command, "CREATE").Result;
            bResult = task.Item1;

            if (_msg == "1")
                _elog.Debug($"Se ha creado la Base de Datos {_bdEve} correctamente.");
            else if (_msg == "2")
                _elog.Debug($"Se confirma la existencia de la Base de Datos {_bdEve}.");

            //2do Chequeo si existe la tabla de evento, de lo contrario la crea.
            if (bResult)
            {
                command = new SqlCommand();
                command.CommandText = "IF OBJECT_ID('@evento','U') IS NULL " + 
                                      "BEGIN " +
                                      "CREATE TABLE @evento (" +
                                      "@mine_num int IDENTITY(1,1) PRIMARY KEY," +
                                      "@mine_est int not null, " +
                                      "@mine_via int not null, " +
                                      "@mine_nsec int not null, " +
                                      "@mine_feven datetime not null, " +
                                      "@mine_desc varchar(max) not null, " +
                                      "@mine_state varchar(20) not null, " +
                                      "@mine_try int not null, " +
                                      "@mine_json varchar(max) null, " +
                                      "@mine_error varchar(max) null, " +
                                      "@mine_ferei datetime not null default(CURRENT_TIMESTAMP)," +
                                      "INDEX evestate NONCLUSTERED (@mine_state), " +
                                      "INDEX evefeven NONCLUSTERED (@mine_feven)); " +
                                      "PRINT 1 " + 
                                      "END " + 
                                      "ELSE " + 
                                      "BEGIN " +
                                      "IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'evestate' AND object_id = OBJECT_ID('@evento')) " +
                                      "BEGIN " +
                                      "CREATE NONCLUSTERED INDEX evestate on @evento(@mine_state) " +
                                      "END " +
                                      "IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'evefeven' AND object_id = OBJECT_ID('@evento')) " +
                                      "BEGIN " +
                                      "CREATE NONCLUSTERED INDEX evefeven on @evento(@mine_feven) " +
                                      "END " +
                                      "PRINT 2 " +
                                      "END";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                task = EjecutaConsulta(_connectionString, command, "CREATE").Result;
                bResult = task.Item1;

                if (_msg == "1")
                    _elog.Debug($"Se ha creado la tabla {_tbEve} correctamente.");
                else if (_msg == "2")
                {
                    _elog.Debug($"Se confirma la existencia de la tabla {_tbEve}.");
                    //Revisamos si incluye la columna de ultimo reintento de envio
                    command = new SqlCommand();
                    command.CommandText = "IF NOT EXISTS( " +
                                          "SELECT * " +
                                          "FROM sys.columns " +
                                          "WHERE Name IN (N'@mine_ferei') " +
                                          "AND Object_ID = Object_ID(N'@evento')) " +
                                          "BEGIN " +
                                          "ALTER TABLE Evento " +
                                          "ADD eve_ferei Datetime not null Default(CURRENT_TIMESTAMP) " +
                                          "END";

                    command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                    command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                    task = EjecutaConsulta(_connectionString, command, "CREATE").Result;
                    bResult = task.Item1;

                    if (bResult)
                        _elog.Info("Se confirma existencia de col de fecha de reintentos...");
                }
            }

            //3ero Chequeo si existe la tabla de online, de lo contrario la crea.
            if (bResult)
            {
                command = new SqlCommand();
                command.CommandText = "IF OBJECT_ID('@online','U') IS NULL " +
                                      "BEGIN CREATE TABLE @online (" +
                                      "@mino_est int not null, " +
                                      "@mino_via int not null, " +
                                      "@mino_nsec int not null, " +
                                      "@mino_feven datetime not null, " +
                                      "@mino_desc varchar(max) not null, " +
                                      "@mino_state varchar(20) not null, " +
                                      "@mino_json varchar(max) null, " +
                                      "@mino_error varchar(max) null); " +
                                      "PRINT 1 " +
                                      "END " +
                                      "ELSE " +
                                      "BEGIN " +
                                      "PRINT 2 " +
                                      "END";
                command.CommandText = command.CommandText.Replace("@online", _tbOnl);
                command.CommandText = command.CommandText.Replace("@mino", _tbOnlMin);
                task = EjecutaConsulta(_connectionString, command, "CREATE").Result;
                bResult = task.Item1;

                if (_msg == "1")
                    _elog.Debug($"Se ha creado la tabla {_tbOnl} correctamente.");
                else if (_msg == "2")
                    _elog.Debug($"Se confirma la existencia de la tabla {_tbOnl}.");
            }

            if (bResult)
            {
                SqlConnection.ClearPool(new SqlConnection(sConn));
                _iniTimer.Dispose();
                _elog.Info("Se tienen los elementos necesarios para arrancar el envío de eventos...");
                RecuperarEventos(); //Aqui comienzo funcion de recuperación, hasta que no recupere todo no inicio el exec
                SetTimerEventExec();
                //SetTimerEventPrioridadMedia();
                //SetTimerEventSinPrioridadExec();
                SetTimerRecuperaEve();
                _elog.Info("Inician todos los timers.");
            }
            else
            {
                _elog.Info("No se tienen los elementos necesarios para arrancar el envío de eventos... Reintentando...");
                _iniTimer.Start();
            }

            _elog.Trace("Salgo");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento desencadenado por el timer de ejecución, comienza a recuperar la lista de los eventos
        /// PENDIENTE, REINTENTO, y los ejecuta
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            _elog.Trace("Entro");
            bool bResult = false;
            _aTimerPrioridad.Stop();//detengo el timer

            try
            {
                eEstado enuEstado = eEstado.PENDIENTE;
                List<Evento> listaEvento = new List<Evento>();
                SqlCommand command = new SqlCommand();

                if (!Conexion)
                {
                    _elog.Debug("No hay conexion al servidor, me salgo...");
                    _aTimerPrioridad.Start();
                    return;
                }

                _elog.Trace("Conexion OK");

                #region 1.PENDIENTE
                //1ero consulto todos los eventos con estado PENDIENTE de la tabla Evento y el ultimo de la tabla Online
                command.CommandText = "select first.@mine_num, first.@mine_desc, first.@mine_state, first.@mine_try from (select @mine_num, @mine_desc, @mine_state, @mine_try from @evento WITH(INDEX(evestate)) where @mine_state = @estado and @mine_via = @nvia) first union all select last.@mino_nsec, last.@mino_desc, last.@mino_state, last.@mino_try from (select top 1 @mino_nsec, @mino_desc, @mino_state, Null as @mino_try from @online where @mino_state = @estado and @mino_via = @nvia) last order by first.@mine_num";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@online", _tbOnl);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.CommandText = command.CommandText.Replace("@mino", _tbOnlMin);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@nvia", Value = _con.Via, SqlDbType = SqlDbType.Int });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento);

                if (listaEvento.Any())
                    EventoPendiente = true;
                else
                    EventoPendiente = false;

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS PENDIENTES: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        if (Eventos.Intentos >= _retries)
                        {
                            enuEstado = eEstado.FALLA_SP;

                            SqlCommand commandUpdate = new SqlCommand();
                            commandUpdate.CommandText = "UPDATE @evento set @mine_state = @estado where @mine_num = @evenumero";
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@evento", _tbEve);
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@mine", _tbEveMin);
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@evenumero", Value = Eventos.Numero, SqlDbType = SqlDbType.Int });

                            var task = EjecutaConsulta(_connectionString, commandUpdate, "UPDATE").Result;

                            continue;
                        }

                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                    EventoPendiente = false;
                }
                #endregion

                #region 2.REINTENTO
                //2do consulto los eventos estado = REINTENTO
                listaEvento.Clear();
                enuEstado = eEstado.REINTENTO;

                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try, @mine_feven FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento, true);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS REINTENTO: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        if (Eventos.Intentos >= _retries)
                        {
                            enuEstado = eEstado.FALLA_SP;

                            SqlCommand commandUpdate = new SqlCommand();
                            commandUpdate.CommandText = "UPDATE @evento set @mine_state = @estado where @mine_num = @evenumero";
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@evento", _tbEve);
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@mine", _tbEveMin);
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@evenumero", Value = Eventos.Numero, SqlDbType = SqlDbType.Int });

                            var task = EjecutaConsulta(_connectionString, commandUpdate, "UPDATE").Result;

                            continue;
                        }

                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion

                #region 3.RECUPERADO
                //3ero consulto los eventos estado = RECUPERADO estos solo se pueden ejecutar si ya pasó 10 seg desde el ultimo reintento
                listaEvento.Clear();
                enuEstado = eEstado.RECUPERADO;

                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado AND DATEDIFF(SECOND,@mine_ferei,getdate()) >= 10";
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS RECUPERADO: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion

                #region 4.FALLA_SP una falla
                //4to consulto los eventos estado = FALLA_SP estos solo se pueden ejecutar si ya pasó 10 seg desde el ultimo reintento
                //y si solo ha fallado 1 vez
                listaEvento.Clear();
                enuEstado = eEstado.FALLA_SP;
                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try, @mine_feven FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado AND DATEDIFF(SECOND,@mine_ferei,getdate()) >= 10 AND @mine_try <= 1";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento, true);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS FALLA_SP con 1 falla: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        double days = Math.Abs((DateTime.Now - Eventos.Fecha).TotalDays);

                        if (days >= _failedDay)
                        {
                            //se cambia el estado de este evento, porque ya es muy viejo y nunca se pudo enviar
                            enuEstado = eEstado.RECHAZADO;

                            SqlCommand commandUpdate = new SqlCommand();
                            commandUpdate.CommandText = "UPDATE @evento set @mine_state = @estado where @mine_num = @evenumero";
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@evento", _tbEve);
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@mine", _tbEveMin);
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@evenumero", Value = Eventos.Numero, SqlDbType = SqlDbType.Int });

                            var task = EjecutaConsulta(_connectionString, commandUpdate, "UPDATE").Result;

                            continue;
                        }

                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion

                #region 5.FALLA_PARAM una falla
                //5to consulto los eventos estado = FALLA_PARAM estos solo se pueden ejecutar si ya pasó 10 seg desde el ultimo reintento
                //y si solo ha fallado 1 vez
                listaEvento.Clear();
                enuEstado = eEstado.FALLA_PARAM;

                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado AND DATEDIFF(SECOND,@mine_ferei,getdate()) >= 10 AND @mine_try <= 1";
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS FALLA_PARAM con 1 falla: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion

                #region 6.FALLA_SP
                //6to consulto los eventos estado = FALLA_SP
                listaEvento.Clear();
                enuEstado = eEstado.FALLA_SP;

                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try, @mine_feven FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado AND DATEDIFF(MINUTE,@mine_ferei,getdate()) >= @tiempo";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@tiempo", Value = _con.MinutosReintentoFallidos, SqlDbType = SqlDbType.Int });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento, true);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS FALLA_SP: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        double days = Math.Abs((DateTime.Now - Eventos.Fecha).TotalDays);

                        if (days >= _failedDay)
                        {
                            //se cambia el estado de este evento, porque ya es muy viejo y nunca se pudo enviar
                            enuEstado = eEstado.RECHAZADO;

                            SqlCommand commandUpdate = new SqlCommand();
                            commandUpdate.CommandText = "UPDATE @evento set @mine_state = @estado where @mine_num = @evenumero";
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@evento", _tbEve);
                            commandUpdate.CommandText = commandUpdate.CommandText.Replace("@mine", _tbEveMin);
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                            commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@evenumero", Value = Eventos.Numero, SqlDbType = SqlDbType.Int });

                            var task = EjecutaConsulta(_connectionString, commandUpdate, "UPDATE").Result;

                            continue;
                        }

                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion

                #region 7.FALLA_PARAM
                //7mo consulto los eventos estado = FALLA_PARAM
                listaEvento.Clear();
                enuEstado = eEstado.FALLA_PARAM;

                command = new SqlCommand();
                command.CommandText = "SELECT @mine_num, @mine_desc, @mine_state, @mine_try FROM @evento WITH(INDEX(evestate)) WHERE @mine_state = @estado AND DATEDIFF(MINUTE,@mine_ferei,getdate()) >= @tiempo";
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@tiempo", Value = _con.MinutosReintentoFallidos, SqlDbType = SqlDbType.Int });

                bResult = RecuperaLista(command, _tbEveMin, ref listaEvento);

                if (listaEvento.Any())
                    _elog.Trace("EVENTOS FALLA_PARAM: {0}", listaEvento.Count);

                if (bResult)
                {
                    foreach (Evento Eventos in listaEvento)
                    {
                        if (!EvaluaEvento(Eventos))
                        {
                            _aTimerPrioridad.Start();
                            return;
                        }
                    }
                }
                #endregion
            }
            catch(Exception ex)
            {
                _elog.Error(ex.ToString());
            }

            _aTimerPrioridad.Start();

            _elog.Trace("Salgo");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento desencadenado por el timer de borrado, comienza a verificar si en la BD local existen
        /// eventos que hayan superado el máximo de dias de permanencia establecidos en la App.config y 
        /// los elimina de la tabla.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private static void OldEventDelete(Object source, ElapsedEventArgs e)
        {
            _elog.Trace("Entro...");
            _deleteEventsTimer.Stop();

            try
            {
                if (_deleteEventsTimer.Interval == 100)
                {
                    TimeSpan tsConfig = Configuraciones.Instance.Configuracion.HoraBorrarEvento;
                    DateTime dtAhora = DateTime.Now;
                    DateTime dtConfig = new DateTime(dtAhora.Year, dtAhora.Month, dtAhora.Day, tsConfig.Hours, tsConfig.Minutes, tsConfig.Seconds);

                    if (dtAhora.TimeOfDay != tsConfig)
                    //determinar cuanto tiempo falta para ejecutar timer
                    {
                        TimeSpan ts24 = new TimeSpan(24, 0, 0);
                        TimeSpan tsDiff = dtConfig.Subtract(dtAhora);
                        //Si el resultado es negativo, restamos al TS 24 para sacar el tiempo faltante
                        tsDiff = (tsDiff.Duration() != tsDiff) ? ts24.Subtract(tsDiff.Duration()) : tsDiff;
                        _deleteEventsTimer.Interval = tsDiff.TotalMilliseconds;
                    }
                    else
                    {
                        _deleteEventsTimer.Enabled = true;
                        _deleteEventsTimer.Interval = MILLISECONDS;
                        _deleteEventsTimer.Enabled = false;
                    }
                }
                else
                {
                    _deleteEventsTimer.Enabled = true;
                    _deleteEventsTimer.Interval = MILLISECONDS;
                    _deleteEventsTimer.Enabled = false;
                }

                if (_deleteEventsTimer.Interval == MILLISECONDS || _deleteEventsTimer.Interval == 1000)
                {
                    BorrarEventosViejos(_tbEve, _eveDay);
                    //BorrarEventosViejos(_tbOnl, _onlDay); //GAB: se comenta porque no hace falta borrar online
                }
                else
                    _elog.Info("Hay que esperar {0} {1} para disparar el Timer de borrado de eventos",
                                _deleteEventsTimer.Interval < 60000 ? (_deleteEventsTimer.Interval / 1000).ToString("F") : (_deleteEventsTimer.Interval / 60000).ToString("F"),
                                _deleteEventsTimer.Interval < 60000 ? "segundos" : "minutos");
            }
            catch (Exception ex)
            {
                _elog.Error(ex.ToString());
            }

            _deleteEventsTimer.Start();
            _elog.Trace("Salgo...");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evento desencadenado por el timer de recuperación de eventos, comienza a verificar si en el disco
        /// hay archivos de eventos que no hayan sido guardados en la BD local, estos eventos deben tener un
        /// minimo de antiguedad establecido
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        private void OnRecuperaEvent(Object source, ElapsedEventArgs e)
        {
            _elog.Trace("Entro...");
            _recuperaEve.Stop();

            try
            {
                string sDesde = "", sHasta = "", sLog = "";
                sDesde = DateTime.Now.AddMinutes(-_con.MinutosAntiguedadEventos).ToString();
                sHasta = DateTime.Now.ToString();

                sLog = RecuperarEventos(sDesde, sHasta);
                //_elog.Info(sLog);
            }
            catch (Exception ex)
            {
                _elog.Error(ex.ToString());
            }

            _recuperaEve.Start();
            _elog.Trace("Salgo...");
        }
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Evalúa la solicitud del cliente de vía
        /// </summary>
        /// <param name="solicitud">Solicitud del cliente de via</param>
        /// <returns>Resultados de la ejecución de la solicitud</returns>
        /// ************************************************************************************************
        public async Task<RespuestaBaseDatos> SolicitudCliente(SolicitudEnvioEventos solicitud, bool esViaEscape)
        {
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            string sLog = string.Empty;
            bool bVal;
            
            //El cliente indica que se quieren recuperar eventos
            if (solicitud.AccionEvento == eAccionEventoBD.Recuperar)
            {
                if (solicitud.FechasRecuperacion.Contains('|'))
                {
                    string[] fechas = solicitud.FechasRecuperacion.Split('|');
                    sLog = RecuperarEventos(fechas[0], fechas[1], esViaEscape);
                    respuesta.CodError = EnmErrorBaseDatos.Recuperacion;
                }
                else if (!string.IsNullOrEmpty(solicitud.FechasRecuperacion))
                {
                    sLog = RecuperarEventos(solicitud.FechasRecuperacion, string.Empty, esViaEscape);
                    if(string.IsNullOrEmpty(sLog))
                        respuesta.CodError = EnmErrorBaseDatos.Falla;
                }
                else
                    respuesta.CodError = EnmErrorBaseDatos.Falla;

                respuesta.RespuestaDB = sLog;
            }
            //No se especifico una accion
            else if (solicitud.AccionEvento == eAccionEventoBD.Default)
                respuesta.CodError = EnmErrorBaseDatos.ErrorAccion;
            //El cliente indica que se quiere almacenar un evento
            else
            {
                bVal = await GuardarEvento(solicitud);

                if (bVal)
                    respuesta.CodError = EnmErrorBaseDatos.EventoAlmacenado;
                else
                    respuesta.CodError = EnmErrorBaseDatos.EventoNoAlmacenado;
            }

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);

            return respuesta;
        }

        #region Gestion de Eventos
        /// ************************************************************************************************
        /// <summary>
        /// Recibe el evento generado del TCI y lo almacena en la BD local
        /// </summary>
        /// <param name="dbParam">Solicitud de la via</param>
        /// <returns>True si almacenó el evento, de lo contario False</returns>
        /// ************************************************************************************************
        private async Task<bool> GuardarEvento(SolicitudEnvioEventos dbParam)
        {
            _elog.Trace("Entro");
            bool bExec = false;
            SqlCommand command;
            eEstado enuEstado;
            string sSecuencia = "";

            if (string.IsNullOrEmpty(dbParam.SqlString) || dbParam.SqlString.Length <= 20)
            {
                _elog.Debug("El evento está vacío o no tiene la longitud necesaria: {0}", dbParam.SqlString);
                return bExec;
            }

            //lo guardo normal, se duplican las comillas simples ' > '' para que se pueda guardar en la base
            //segun el tipo de evento se remueven caracteres o se reemplazan..
            dbParam.SqlString = ConvertSP(dbParam.SqlString, dbParam.Tipo);
            sSecuencia = dbParam.Secuencia.ToString();

            //si la longitud de la secuencia es mayor a 9 (maximo permitido en tipo de dato INT de la BD)
            //tenemos que tomar los ultimos 9 digitos
            if (sSecuencia.Length > 9)
                sSecuencia = sSecuencia.Substring(Math.Abs(sSecuencia.Length - 9));

            //Almacena en tabla de Evento
            if (dbParam.Tipo == eTipoEventoBD.Evento)
            {
                //Ejemplo de Nombre EVE_201803201330169283800610410.dat
                enuEstado = eEstado.PENDIENTE;

                command = new SqlCommand();
                command.CommandText = "INSERT INTO @evento VALUES (@estacion,@via,@secuencia,@fecha,@sqlstring,@estado,@try,@objetos,null,@fechaRe)";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int });
                if(!dbParam.EsViaEscape)
                    command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int });
                else
                    command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.NumeroViaEscape, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@secuencia", Value = sSecuencia, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@fecha", Value = dbParam.FechaGenerado, SqlDbType = SqlDbType.DateTime });
                command.Parameters.Add(new SqlParameter { ParameterName = "@sqlstring", Value = dbParam.SqlString, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@try", Value = 0, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@objetos", Value = string.IsNullOrEmpty(dbParam.Objetos) ? DBNull.Value : (object)dbParam.Objetos, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@fechaRe", Value = DateTime.Now, SqlDbType = SqlDbType.DateTime });

                var tuple = await EjecutaConsulta(_connectionString, command, "INSERT");
                bExec = tuple.Item1;
            }
            //Almacena en tabla de Online
            else if (dbParam.Tipo == eTipoEventoBD.Online)
            {
                enuEstado = eEstado.PENDIENTE;

                if (!_bOnlineReg)
                {
                    command = new SqlCommand();
                    command.CommandText = "SELECT onl_via, onl_est FROM @online WHERE onl_via = @via AND onl_est = @estacion";
                    command.CommandText = command.CommandText.Replace("@online", _tbOnl);
                    command.Parameters.Add(new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int });

                    if (!dbParam.EsViaEscape)
                        command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int });
                    else
                        command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.NumeroViaEscape, SqlDbType = SqlDbType.Int });

                    var task = await EjecutaConsulta(_connectionString, command, "CONSULTA");
                    _bOnlineReg = task.Item1;
                }

                //si existe hago update
                if(_bOnlineReg)
                {
                    command = new SqlCommand();
                    command.CommandText = "UPDATE @online set onl_nsec = @secuencia, onl_feven = @fecha, " +
                                                 "onl_desc = @sqlstring, onl_state = @estado, onl_json = @objetos " +
                                          "where onl_est = @estacion AND onl_via = @via";

                    command.CommandText = command.CommandText.Replace( "@online", _tbOnl );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int } );

                    if( !dbParam.EsViaEscape )
                        command.Parameters.Add( new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int } );
                    else
                        command.Parameters.Add( new SqlParameter { ParameterName = "@via", Value = _con.NumeroViaEscape, SqlDbType = SqlDbType.Int } );

                    command.Parameters.Add( new SqlParameter { ParameterName = "@secuencia", Value = dbParam.Secuencia, SqlDbType = SqlDbType.Int } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@fecha", Value = dbParam.FechaGenerado, SqlDbType = SqlDbType.DateTime } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@sqlstring", Value = dbParam.SqlString, SqlDbType = SqlDbType.VarChar } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@objetos", Value = string.IsNullOrEmpty( dbParam.Objetos ) ? DBNull.Value : (object)dbParam.Objetos, SqlDbType = SqlDbType.VarChar } );

                    var task = await EjecutaConsulta( _connectionString, command, "UPDATE");
                    bExec = task.Item1;
                }
                // no existe, lo inserta
                else
                {
                    command = new SqlCommand();
                    command.CommandText = "INSERT INTO @online VALUES (@estacion,@via,@secuencia,@fecha,@sqlstring,@estado,@objetos,null)";
                    command.CommandText = command.CommandText.Replace( "@online", _tbOnl );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int } );
                    if( !dbParam.EsViaEscape )
                        command.Parameters.Add( new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int } );
                    else
                        command.Parameters.Add( new SqlParameter { ParameterName = "@via", Value = _con.NumeroViaEscape, SqlDbType = SqlDbType.Int } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@secuencia", Value = dbParam.Secuencia, SqlDbType = SqlDbType.Int } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@fecha", Value = dbParam.FechaGenerado, SqlDbType = SqlDbType.DateTime } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@sqlstring", Value = dbParam.SqlString, SqlDbType = SqlDbType.VarChar } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar } );
                    command.Parameters.Add( new SqlParameter { ParameterName = "@objetos", Value = string.IsNullOrEmpty( dbParam.Objetos ) ? DBNull.Value : (object)dbParam.Objetos, SqlDbType = SqlDbType.VarChar } );

                    var tuple = await EjecutaConsulta(_connectionString, command, "INSERT");
                    bExec = tuple.Item1;
                }
            }

            if (bExec && dbParam.Secuencia != 0 && dbParam.Tipo == eTipoEventoBD.Evento)
                MoverArchivoEvento(dbParam.EsViaEscape, dbParam);

            if(!bExec)
                _elogReceived.Info("No se guardó el evento [{0}] secuencia numero: {1}", dbParam.SqlString, dbParam.Secuencia);

            _elog.Trace("Salgo");
            return bExec;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Recupera los eventos almacenados en la carpeta de eventos, verifica si están en la BD local y
        /// los almacena con status RECUPERADO para su posterior ejecución.
        /// </summary>
        /// <param name="sDesde">Fecha Desde</param>
        /// <param name="sHasta">Fecha Hasta</param>
        /// <returns>Conteo de los eventos que se pudieron recuperar</returns>
        /// ************************************************************************************************
        private string RecuperarEventos(string sDesde = "", string sHasta = "", bool esViaEscape = false)
        {
            _elog.Trace("Entro...");
            string sComando = "", sFecha = "", sSecuencia = "", sLog, sViaEst= "";
            bool bRes = false;
            DateTime dtUltimoEve = DateTime.MinValue, dtDesde = DateTime.MinValue, dtHasta = DateTime.MinValue, dtFechaFile = DateTime.MinValue;
            int nRecuperados = 0, nCorruptos = 0, nGuardados = 0;
            DirectoryInfo dir = new DirectoryInfo(_evePath);
            List<FileInfo> posiblesEve = new List<FileInfo>();
            SqlCommand command;
            eEstado enuEstado = eEstado.RECUPERADO;

            if (Directory.Exists(_evePath))
            {
                //Primero revisa si hay archivos de eventos sin clasificar en la carpeta principal
                FileInfo[] evePrincipal = dir.EnumerateFiles().Select(x => { x.Refresh(); return x; }).ToArray();
                foreach (FileInfo ev in evePrincipal)
                {
                    //si hay eventos los mueve a carpeta correspondiente del dia
                    MoverArchivoEvento(esViaEscape, null, ev);
                }

                if (String.IsNullOrEmpty(sDesde) && String.IsNullOrEmpty(sHasta))
                {
                    //Se ubica la fecha a recuperar eventos partiendo
                    //de la fecha actual hacia atras hasta LOCAL_MAX_RECOVERY_EVENT_DAYS
                    dtUltimoEve = DateTime.Now.AddDays(-_con.MaxDiasEventosRecuperar);

                    while (dtUltimoEve.Date <= DateTime.Now.Date)
                    {
                        string sPathDate = Path.Combine(_evePath, dtUltimoEve.ToString("yyyy-MM-dd"));

                        if (Directory.Exists(sPathDate))
                        {
                            string viaEst = "";
                            if (!esViaEscape)
                                viaEst = _viaEst;
                            else
                                viaEst = _viaEscEst;
                            DirectoryInfo dirAux = new DirectoryInfo(sPathDate);
                            //Buscar en disco si hay eventos que contengan misma via y est
                            posiblesEve.AddRange(dirAux.EnumerateFiles()
                                                       .Select(x => { x.Refresh(); return x; })
                                                       .Where(f => f.Name.Contains(viaEst) &&
                                                                   f.Name.Contains(_tbEveMin.ToUpper() + "_")));
                        }

                        //agrega un dia para moverse por carpeta
                        dtUltimoEve = dtUltimoEve.AddDays(1);
                    }
                    dtUltimoEve = DateTime.Now;
                }
                else if (!string.IsNullOrEmpty(sDesde) && string.IsNullOrEmpty(sHasta))
                {
                    //cambio el estado de los eventos SUSPENDIDO a PENDIENTE para volverlos a ejecutar
                    eEstado enuFiltroEstado = eEstado.SUSPENDIDO;
                    enuEstado = eEstado.PENDIENTE;
                    string sUpdate = "";
                    dtDesde = DateTime.Parse(sDesde);

                    SqlCommand commandUpdate = new SqlCommand();
                    commandUpdate.CommandText = "UPDATE @evento set @mine_state = @estadoNuevo where @mine_state = @estadoViejo AND @mine_feven >= @fecha";
                    commandUpdate.CommandText = commandUpdate.CommandText.Replace("@evento", _tbEve);
                    commandUpdate.CommandText = commandUpdate.CommandText.Replace("@mine", _tbEveMin);
                    commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estadoNuevo", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                    commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@estadoViejo", Value = enuFiltroEstado, SqlDbType = SqlDbType.VarChar });
                    commandUpdate.Parameters.Add(new SqlParameter { ParameterName = "@fecha", Value = dtDesde, SqlDbType = SqlDbType.DateTime });

                    var task = EjecutaConsulta(_connectionString, commandUpdate, "UPDATE").Result;
                    bRes = task.Item1;
                    sUpdate = task.Item2;

                    if (bRes)
                        sLog = $"Se reintentan {sUpdate} eventos fallidos (SUSPENDIDOS) con fecha desde: {sDesde}";
                    else
                        sLog = "";

                    _elog.Info(sLog);

                    return sLog;
                }
                else
                {
                    //Convierte a DateTime para comparar mas facil contra la fecha del archivo
                    dtDesde = DateTime.Parse(sDesde);
                    dtHasta = DateTime.Parse(sHasta);

                    DateTime datAuDesde = dtDesde;
                    while (datAuDesde.Date <= dtHasta.Date)
                    {
                        string sPathDate = Path.Combine(_evePath, datAuDesde.ToString("yyyy-MM-dd"));

                        if (Directory.Exists(sPathDate))
                        {
                            string viaEst = "";
                            if (!esViaEscape)
                                viaEst = _viaEst;
                            else
                                viaEst = _viaEscEst;
                            DirectoryInfo dirAux = new DirectoryInfo(sPathDate);
                            //Buscar en disco si hay eventos que contengan misma via y est
                            posiblesEve.AddRange(dirAux.EnumerateFiles()
                                                       .Select(x => { x.Refresh(); return x; })
                                                       .Where(f => f.Name.Contains(viaEst) &&
                                                                   f.Name.Contains(_tbEveMin.ToUpper() + "_")));
                        }

                        //agrega un dia para moverse por carpeta
                        datAuDesde = datAuDesde.AddDays(1);
                    }
                }

                //recorre lista de eventos de disco para determinar si la hora es posterior a la ultima de la BD, en caso
                //de recuperar eventos al inicio. Si hay fecha desde y hasta especificada, se tomaran todos los eventos que coincidan
                foreach (FileInfo ev in posiblesEve)
                {
                    string sExtension = ev.Extension;
                    if (sExtension.ToUpper() == ".DAT")
                    {
                        string sArchivo = ev.Name;
                        string sNombre = sArchivo.Substring(0, 4).ToUpper();
                        //Ejemplo de Nombre EVE_201803201330169283800610410.dat
                        //EVE_  2018    03  20  13      30  16  928     38006   104 10  .dat
                        //EVE_  AÑO     MES DIA HORA    MIN SEG MS      SEC     VIA EST .ext
                        string[] datos = sArchivo.Split(new Char[] { '_', '.' });

                        //Leo el evento y extraigo la sentencia SQL
                        sComando = File.ReadAllText(ev.FullName);
                        if (string.IsNullOrEmpty(sComando) || sComando.Length <= 20)
                        {
                            nCorruptos++;
                            _elog.Debug("El evento recuperado está vacío o no tiene la longitud necesaria: Secuencia[{0}], Sqlstring{1}", datos[1].Substring(17, 5), sComando);
                            continue;
                        }

                        //El nombre es correcto
                        if (datos.Length > 2)
                        {
                            try
                            {
                                //calculamos la posición para tomar la via y est
                                int iPosViaEst = datos[1].Length - 5;
                                //calculamos cual es la longitud de la secuencia
                                int iLongSec = Math.Abs(iPosViaEst - 17);

                                sFecha = datos[1].Substring(0, 17);
                                sSecuencia = datos[1].Substring(17, iLongSec);
                                //si la longitud de la secuencia es mayor a 9 (maximo permitido en tipo de dato INT de la BD)
                                //tenemos que tomar los ultimos 9 digitos
                                if (sSecuencia.Length > 9)
                                    sSecuencia = sSecuencia.Substring(Math.Abs(sSecuencia.Length - 9));
                                sViaEst = datos[1].Substring(iPosViaEst);

                                sFecha = FormatoFechaBd(sFecha);
                                dtFechaFile = new SqlDateTime(DateTime.Parse(sFecha)).Value;
                                //sFecha = dtFechaFile.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                            }
                            catch (Exception e)
                            {
                                _elog.Error("Excepcion al extraer datos del nombre del archivo. Archivo [{0}] {1}", sArchivo, e.ToString());
                            }

                            //Si no se cumplen estas condiciones se pasa al siguiente...
                            if (String.IsNullOrEmpty(sDesde) || String.IsNullOrEmpty(sHasta))
                            {
                                if (dtFechaFile.Date < dtUltimoEve.Date.AddDays(-_con.MaxDiasEventosRecuperar))
                                    continue;
                            }
                            else
                            {
                                 if (dtFechaFile < dtDesde || dtFechaFile > dtHasta)
                                    continue;
                            }

                            //si la Via y Est no son iguales, ignora el evento
                            string viaEst = "";
                            if (!esViaEscape)
                                viaEst = _viaEst;
                            else
                                viaEst = _viaEscEst;
                            if (sViaEst != viaEst)
                                continue;
                        }
                        else
                            continue;

                        if (sNombre == _tbEveMin.ToUpper() + "_" && !(string.IsNullOrEmpty(sFecha) && string.IsNullOrEmpty(sSecuencia)))
                        {
                            //1. Hago un select a la tabla para ver si ya existe en la bd local el evento

                            command = new SqlCommand();
                            command.CommandText = "SELECT @mine_feven FROM @evento WITH(INDEX(evefeven)) WHERE @mine_feven = @fecha AND @mine_nsec = @secuencia";
                            command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                            command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                            command.Parameters.Add(new SqlParameter { ParameterName = "@fecha", Value = dtFechaFile, SqlDbType = SqlDbType.DateTime });
                            command.Parameters.Add(new SqlParameter { ParameterName = "@secuencia", Value = sSecuencia, SqlDbType = SqlDbType.Int });

                            var task = EjecutaConsulta(_connectionString, command, "CONSULTA").Result;
                            bRes = task.Item1;

                            //si existe, incremento n guardados para el log
                            if (bRes)
                            {
                                nGuardados++;
                            }
                            //no existe, lo inserta
                            else
                            {
                                sComando = ConvertSP(sComando, eTipoEventoBD.Evento);
                                sComando = ConvertSP(sComando, eTipoEventoBD.FueraSecuencia);
                                enuEstado = eEstado.RECUPERADO;
                                command = new SqlCommand();
                                command.CommandText = "INSERT INTO @evento VALUES (@estacion,@via,@secuencia,@fecha,@sqlstring,@estado,@try,null,null,@fechaRe)";
                                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                                command.Parameters.Add(new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@secuencia", Value = sSecuencia, SqlDbType = SqlDbType.Int });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@fecha", Value = dtFechaFile, SqlDbType = SqlDbType.DateTime });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@sqlstring", Value = sComando, SqlDbType = SqlDbType.VarChar });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@try", Value = 0, SqlDbType = SqlDbType.Int });
                                command.Parameters.Add(new SqlParameter { ParameterName = "@fechaRe", Value = DateTime.Now, SqlDbType = SqlDbType.DateTime });

                                task = EjecutaConsulta(_connectionString, command, "INSERT").Result;
                                bRes = task.Item1;

                                if(bRes)
                                    nRecuperados++;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(sDesde) && string.IsNullOrEmpty(sHasta))
                    sLog = $"De los eventos por recuperar al inicio, {nGuardados} ya estaba(n)" +
                           $" almacenado(s) en la BD. {nCorruptos} estaba(n) corrupto(s) y {nRecuperados} fue(ron) recuperado(s)" +
                           "de disco...";
                else
                    sLog = $"Recuperación de eventos desde {sDesde} hasta {sHasta}. Eventos que ya estaban en la BD local: {nGuardados}, " +
                           $"recuperados de disco: {nRecuperados}, eventos corruptos: {nCorruptos}. " +
                           $"Total recuperados: {nRecuperados}";

            }
            else
                sLog = "No se pudo encontrar el directorio especificado para los Eventos guardados en disco";

            _elog.Info(sLog);
            _elog.Trace("Salgo...");
            return sLog;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Determina si el evento debe ser ejecutado o no, también proporciona información luego de
        /// su ejecución
        /// </summary>
        /// <param name="Eventos">Evento por evaluar</param>
        /// <returns>True si todo salió bien, de lo contrario False</returns>
        /// ************************************************************************************************
        private bool EvaluaEvento(Evento oEventos)
        {
            _elog.Trace("Entro...");
            bool bRet = false, bReintentando = false;
            eEjecucion Res = eEjecucion.SinError;
            string sError = "", sComando = "", sErrorString = "";
            sComando = oEventos.SqlString;
            int iTry = 0;
            SqlCommand command = new SqlCommand();
            eEstado enuEstado;

            //Si el estado es ENVIADO no lo tomo en cuenta...
            if (oEventos.Estado == eEstado.ENVIADO)
                return true;
            else if(oEventos.Estado == eEstado.FALLA_PARAM || oEventos.Estado == eEstado.FALLA_SP)
                bReintentando = true;

            //se ejecuta el SP
            if (!string.IsNullOrEmpty(sComando) && sComando.Length > 20)
            {
                Res = EjecutarSP(sComando, out sError);
                _elog.Trace("Ejecutó el SP");
                //Si es evento online solo tiene 2 estados posibles...
                if (sComando.Contains("setEstadoOnline"))
                {
                    if (Res == eEjecucion.SinError)
                    {
                        enuEstado = eEstado.ENVIADO;
                        _elogSended.Info($"Evento online nro. {oEventos.Numero} sin errores[{sComando}]");
                    }
                    else
                    {
                        sErrorString = $"Evento online nro. {oEventos.Numero} con errores: {sError}";
                        enuEstado = eEstado.RECHAZADO;
                    }

                    bRet = true;

                    command.CommandText = "UPDATE @online set @mino_state = @estado, @mino_error = @error where @mino_nsec = @numero";
                    command.CommandText = command.CommandText.Replace("@online", _tbOnl);
                    command.CommandText = command.CommandText.Replace("@mino", _tbOnlMin);
                    command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                    command.Parameters.Add(new SqlParameter { ParameterName = "@error", Value = sErrorString, SqlDbType = SqlDbType.VarChar });
                    command.Parameters.Add(new SqlParameter { ParameterName = "@numero", Value = oEventos.Numero, SqlDbType = SqlDbType.Int });
                }
                //Los eventos normales pueden tener varios estados posibles, se evalúa cada caso...
                else
                {
                    switch (Res)
                    {
                        case eEjecucion.SinError:
                            // Si la ejecución fue correcta
                            if (oEventos.Estado != eEstado.PENDIENTE)
                                _elogErrorSended.Info($"Ejecutado sin errores, Evento No.{oEventos.Numero} [{sComando}]");
                            else
                                _elogSended.Info($"Ejecutado sin errores, Evento No.{oEventos.Numero} [{sComando}]");
                            enuEstado = eEstado.ENVIADO;
                            bRet = true;
                                break;
                        case eEjecucion.errTimeOut:
                            _elogErrorSended.Error($"BD muy lenta falló envío. Error[{Res.ToString()}], Evento No.{oEventos.Numero} [{sError}] [{sComando}]");
                            //Cambio el estado para que se reenvie como fuera de secuencia
                            enuEstado = eEstado.FALLA_SP;
                            sErrorString = $"BD muy lenta falló envío: {sError}";
                            iTry++;
                            break;
                        case eEjecucion.errDeadLock:
                            _elogErrorSended.Error($"DeadLock, falló envío. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                            //conservamos el mismo estado que tenía
                            enuEstado = oEventos.Estado;
                            sErrorString = $"DeadLock falló envío: {sError}";
                            iTry++;
                            break;
                        case eEjecucion.errConexion:
                            _elogErrorSended.Error($"Error de conexión, falló envio. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                            //conservamos el mismo estado que tenía
                            enuEstado = oEventos.Estado;
                            sErrorString = $"Error de conexion falló envío: {sError}";
                            break;
                        case eEjecucion.errSP:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de SP, falló envío. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.FALLA_SP;
                            sErrorString = $"Error de StoredProcedures falló envío: {sError}";
                            bRet = true;
                            break;
                        case eEjecucion.errParametrizacion:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento por parametrización. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.FALLA_PARAM;
                            sErrorString = $"Error de envío de evento por parametrización: {sError}";
                            bRet = true;
                            break;
                        case eEjecucion.errParametrizacionNoExisteTransito:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento, no existe tránsito. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.FALLA_SP;
                            sErrorString = $"Error de envío de evento por que no existe tránsito: {sError}";
                            bRet = true;
                            break;
                        case eEjecucion.errVacio:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento, no existe SP. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.REINTENTO;
                            sErrorString = $"Error de envío de evento, no existe SP: {sError}";
                            bRet = true;
                            break;
                        case eEjecucion.errNoReint:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento, no reintentar. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.SUSPENDIDO;
                            sErrorString = $"Error de envío de evento, no reintentar: {sError}";
                            bRet = true;
                            break;
                        case eEjecucion.Repetido:
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento repetido. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                                if (!sComando.Contains("setErrorSQL") && sError.Contains("No. [2627]"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.REPETIDO;
                            sErrorString = $"Error de envío de evento repetido: {sError}";
                            bRet = true;
                            break;
                        default:
                            //Evitar que al reintentar este SP se agreguen mas ErrorSQL
                            if (oEventos.Estado == eEstado.PENDIENTE)
                            {
                                _elogErrorSended.Error($"Error de envío de evento desconocido. Error[{Res.ToString()}], Evento No.[{oEventos.Numero}] [{sError}] [{sComando}]");
                                if (!sComando.Contains("setErrorSQL"))
                                    GuardarErrorSQL(0, sError + " " + sComando);
                            }
                            enuEstado = eEstado.FALLA_SP;
                            sErrorString = $"Error de envío de evento desconocido: {sError}";
                            bRet = true;
                            break;
                    }

                    if(oEventos.Intentos >= 0 && oEventos.Estado == eEstado.FALLA_SP || oEventos.Estado == eEstado.FALLA_PARAM)
                        iTry++;

                    iTry = iTry == 0 ? oEventos.Intentos : oEventos.Intentos + iTry;

                    //Al dar estas fallas se reemplaza a fuera de secuencia
                    if (enuEstado == eEstado.FALLA_SP || enuEstado == eEstado.FALLA_PARAM || enuEstado == eEstado.SUSPENDIDO)
                    {
                        sComando = ConvertSP(sComando, eTipoEventoBD.FueraSecuencia);

                        command.CommandText = "UPDATE @evento set @mine_state = @estado, @mine_desc = @comando, @mine_try = @try, @mine_error = @error, @mine_ferei = @hora where @mine_num = @numero";
                        command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                        command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                        command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@comando", Value = sComando, SqlDbType = SqlDbType.VarChar });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@try", Value = iTry, SqlDbType = SqlDbType.Int });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@error", Value = sErrorString, SqlDbType = SqlDbType.VarChar });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@hora", Value = DateTime.Now, SqlDbType = SqlDbType.DateTime });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@numero", Value = oEventos.Numero, SqlDbType = SqlDbType.Int });

                        //enviar notificacion del estado de conexion a la via (dependiendo de si dio error o no ejecutar un SP en la estación)
                        if (!bReintentando && CheckConnection.UltimoStatConexion != eEstadoRed.SoloLocal)
                        {
                            CheckConnection.UltimoStatConexion = eEstadoRed.SoloLocal;
                            _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, eEstadoRed.SoloLocal.ToString());
                        }
                    }
                    //se ejecutó correctamente, solo realiza update al Estado
                    else
                    {
                        command.CommandText = "UPDATE @evento set @mine_state = @estado, @mine_try = @try, @mine_error = @error, @mine_ferei = @hora where @mine_num = @numero";
                        command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                        command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                        command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@try", Value = iTry, SqlDbType = SqlDbType.Int });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@error", Value = sErrorString, SqlDbType = SqlDbType.VarChar });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@hora", Value = DateTime.Now, SqlDbType = SqlDbType.DateTime });
                        command.Parameters.Add(new SqlParameter { ParameterName = "@numero", Value = oEventos.Numero, SqlDbType = SqlDbType.Int });

                        //enviar notificacion del estado de conexion a la via (dependiendo de si dio error o no ejecutar un SP en la estación)
                        if (CheckConnection.UltimoStatConexion != eEstadoRed.Ambas)
                        {
                            CheckConnection.UltimoStatConexion = eEstadoRed.Ambas;
                            _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, eEstadoRed.Ambas.ToString());
                        }
                    }
                }
            }
            else
            {
                _elog.Debug("El evento está vacío o no tiene la longitud necesaria: Numero[{0}], Sqlstring{1}", oEventos.Numero, sComando);
                //Si habia algun evento que se almacenó mal en la BD local, le cambiamos el estado para que no lo considere mas.
                enuEstado = eEstado.RECHAZADO;
                command.CommandText = "UPDATE @evento set @mine_state = @estado where @mine_num = @numero";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@numero", Value = oEventos.Numero, SqlDbType = SqlDbType.Int });
            }

            //ejecuta el query, se espera la ejecución
            var task = EjecutaConsulta(_connectionString, command, "UPDATE").Result;
            _elog.Trace("Salgo...");
            return bRet;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Guarda el Error SQL generado cuando no pudo ejecutar el SP
        /// </summary>
        /// <param name="fuesec">Numero fuera de secuencia</param>
        /// <param name="sMensaje">Mensaje a guardar</param>
        /// ************************************************************************************************
        private void GuardarErrorSQL(short fuesec, string sMensaje)
        {
            _elog.Trace("Entro...");
            string sAux, format = "yyyy-MM-ddTHH:mm:ss";
            string sFecha = DateTime.Now.ToString(format);
            int secuencia = 0, nTxtParam = 8000; //nTxtParam es el máximo de longitud del texto en SetErrorSQL
            bool bRet = false;
            SqlCommand command;
            eEstado enuEstado = eEstado.PENDIENTE;

            sMensaje = sMensaje.Replace("'", "|");

            if (sMensaje.Length >= nTxtParam)
                sAux = sMensaje.Substring(0, nTxtParam);
            else
                sAux = sMensaje.Substring(0, sMensaje.Length);

            //Se arma el evento e inserta a la Bd local de acuerdo a la longitud del mensaje
            do
            {
                string sComando = $"exec @RetVal={ERRORSQLSP} {fuesec}, {_con.Estacion}, {_con.Via},'{sFecha}',{secuencia},'{sAux}', 0";

                command = new SqlCommand();
                command.CommandText = "INSERT INTO @evento VALUES (@estacion,@via,@fuesec,@fecha,@comando,@estado,@try,null,null,@fechaRe)";
                command.CommandText = command.CommandText.Replace("@evento", _tbEve);
                command.Parameters.Add(new SqlParameter { ParameterName = "@estacion", Value = _con.Estacion, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@via", Value = _con.Via, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@fuesec", Value = fuesec, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@fecha", Value = sFecha, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@comando", Value = sComando, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@estado", Value = enuEstado, SqlDbType = SqlDbType.VarChar });
                command.Parameters.Add(new SqlParameter { ParameterName = "@try", Value = 0, SqlDbType = SqlDbType.Int });
                command.Parameters.Add(new SqlParameter { ParameterName = "@fechaRe", Value = DateTime.Now, SqlDbType = SqlDbType.DateTime });

                var task = EjecutaConsulta(_connectionString, command, "INSERT").Result;
                bRet = task.Item1;

                if (!bRet)
                    _elogReceived.Info("Error SQL NO almacenado [{0}]", sComando);

                secuencia++;

                if (sMensaje.Length >= nTxtParam)
                {// le saco al mensaje los primeros 20 caracteres y le asigno a sAux los siguientes 20 o lo que le falte para completar el string
                    sMensaje = sMensaje.Substring(nTxtParam);
                    if (sMensaje.Length >= nTxtParam)
                        sAux = sMensaje.Substring(0, nTxtParam);
                    else
                        sAux = sMensaje.Substring(0, sMensaje.Length);
                }
                else
                    sAux = "";

            } while (!string.IsNullOrEmpty(sAux));
            _elog.Trace("Salgo... Secuencia= {0}", secuencia);
        }
        #endregion

        #region Ejecución de consultas
        /// ************************************************************************************************
        /// <summary>
        /// Ejecuta la consulta deseada en la base de datos local indicada
        /// </summary>
        /// <param name="sConnectionString">String de la conexion a la BD</param>
        /// <param name="sConsulta">String de la consulta a realizar</param>
        /// <param name="sTipo">Tipo de consulta</param>
        /// <param name="sRes">Opcional para casos en los que se necesite respuesta</param>
        /// <returns>True si ejecutó la consulta, de lo contario False</returns>
        /// ************************************************************************************************
        private async Task<Tuple<bool,string>> EjecutaConsulta(string sConnectionString, SqlCommand oCommand, string sTipo)
        {
            _elog.Trace("Entro");
            bool bRet = false;
            string sRes = "";

            using (SqlConnection connection = new SqlConnection(sConnectionString))
            using (oCommand)
            {
                try
                {
                    oCommand.Connection = connection;
                    oCommand.CommandTimeout = 2;
                    //Establezco la conexión...
                    await connection.OpenAsync();

                    if (sTipo == "INSERT")
                    {
                        try
                        {
                            await oCommand.ExecuteNonQueryAsync();
                            bRet = true;
                        }
                        catch (SqlException e)
                        {
                            _elog.Error("Excepcion al insertar. {0}:{1}", e.Number, e.Message);
                        }
                    }
                    else if (sTipo == "UPDATE" || sTipo == "DELETE")
                    {
                        oCommand.CommandTimeout = 300;
                        try
                        {
                            int nRow = await oCommand.ExecuteNonQueryAsync();
                            sRes = nRow.ToString();
                            bRet = true;
                        }
                        catch (SqlException e)
                        {
                            _elog.Error("Excepcion al U/D. {0}:{1}]", e.Number, e.Message);
                        }
                    }
                    else if (sTipo == "CREATE")
                    {
                        _msg = "";
                        oCommand.CommandTimeout = 60;
                        oCommand.Connection.InfoMessage += new SqlInfoMessageEventHandler(InfoMessage);
                        try
                        {
                            await oCommand.ExecuteNonQueryAsync();
                            bRet = true;
                        }
                        catch (SqlException e)
                        {
                            _elog.Error("Excepcion Create. {0}:{1}", e.Number, e.Message);
                        }
                    }
                    else if (sTipo == "CONSULTA")
                    {
                        try
                        {
                            object oResult = await oCommand.ExecuteScalarAsync();
                            if (oResult != null)
                            {
                                sRes = oResult.ToString();
                                bRet = true;
                            }
                        }
                        catch (SqlException e)
                        {
                            _elog.Error("Excepcion Consulta. {0}:{1}", e.Number, e.Message);
                        }
                    }
                }
                catch (SqlException e)
                {
                    _elog.Error("Exception {0}:{1}", e.ErrorCode, e.Message);
                }
                catch (Exception e)
                {
                    _elog.Error("General Exception: {0}", e.ToString());
                }
            }
            //WritePerformanceCounters();

            _elog.Trace("Salgo");
            return new Tuple<bool, string>(bRet, sRes);
        }

        /// ************************************************************************************************
        /// <summary>
        /// Recupera la lista de eventos con el Status deseado
        /// </summary>
        /// <param name="sConsulta">Consulta a ejecutar en la BD</param>
        /// <param name="sTipo">Tipo de consulta</param>
        /// <param name="listaEvento">Lista a llenar</param>
        /// <returns>True si recuperó la lista, de lo contrario False</returns>
        /// ************************************************************************************************
        private bool RecuperaLista(SqlCommand oCommand, string sTipo, ref List<Evento> listaEvento, bool bConFecha = false)
        {
            _elog.Trace("Entro");
            bool bRet = false, bParse = false;
            int iAux;

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (oCommand)
            {
                try
                {
                    oCommand.Connection = connection;
                    oCommand.CommandTimeout = 2;
                    //Establezco la conexión...
                    connection.Open();

                    using (SqlDataReader reader = oCommand.ExecuteReader())
                    {
                        if (reader != null)
                        {
                            while (reader.Read())
                            {
                                var evento = new Evento();
                                bParse = int.TryParse(reader[sTipo + "_num"].ToString(), out iAux);
                                evento.Numero = bParse ? iAux : 0;
                                evento.SqlString = reader[sTipo + "_desc"].ToString();
                                evento.Estado = Utility.EstadoEvento(reader[sTipo + "_state"].ToString());
                                bParse = int.TryParse(reader[sTipo + "_try"].ToString(), out iAux);
                                evento.Intentos = bParse ? iAux : 0;

                                if (bConFecha)
                                    evento.Fecha = Convert.ToDateTime(reader[sTipo + "_feven"]);

                                listaEvento.Add(evento);
                            }
                            bRet = true;
                        }
                    }
                }
                catch (SqlException e)
                {
                    _elog.Error("Excepcion {0}:{1}", e.Number, e.Message);
                }
                catch (Exception e)
                {
                    _elog.Error("General Exception: {0}", e.ToString());
                }
            }
            //WritePerformanceCounters();

            _elog.Trace("Salgo");

            return bRet;
        } 

        /// ************************************************************************************************
        /// <summary>
        /// Ejecuta el evento en la base de datos de la estación
        /// </summary>
        /// <param name="sComando">Evento a ejecutar</param>
        /// <param name="sError">Error luego de ejecutar</param>
        /// <returns>Enumerado con el tipo de error</returns>
        /// ************************************************************************************************
        private eEjecucion EjecutarSP(string sComando, out string sError)
        {
            eEjecucion Res = eEjecucion.SinError;
            sError = "";

            try
            {
                using (SqlConnection conn = new SqlConnection())
                {
                    try
                    {
                        conn.ConnectionString = _connectionStringEst;
                        conn.Open();
                    }
                    catch (SqlException sql)
                    {
                        Res = eEjecucion.errConexion;
                        sError = $"Conn ErrorSQL No. [{sql.Number}] Mensaje: {sql.Message}";
                        if (sql.Number == 18487 || sql.Number == 18488)
                            sError += " - Contraseña Vencida";
                    }
                    catch (Exception ex)
                    {
                        Res = eEjecucion.errConexion;
                        sError = $"Conn Exception Mensaje: {ex.Message}";
                    }

                    if (Res == eEjecucion.SinError)
                    {
                        int retval;
                        using (SqlCommand command = new SqlCommand(sComando, conn))
                        {
                            command.CommandTimeout = 30;
                            command.CommandType = CommandType.Text;
                            SqlParameter parRetVal = command.Parameters.Add("@RetVal", SqlDbType.Int);
                            parRetVal.Direction = ParameterDirection.Output;
                            command.ExecuteNonQuery();
                            retval = string.IsNullOrEmpty(parRetVal.Value.ToString()) ? 0 : (int)parRetVal.Value;
                        }
                        _elogSended.Debug($"Respuesta sql retval=[{retval}] Comando [{sComando}]");

                        if (retval == 0)
                        {
                            Res = eEjecucion.SinError;
                        }
                        else if (retval == 1)
                        {
                            Res = eEjecucion.Repetido;
                            sError = $"Retorno[{retval}]";
                        }
                        else if (retval < 0)
                        {
                            if (retval == -107)
                                Res = eEjecucion.errParametrizacionNoExisteTransito;
                            else
                                Res = eEjecucion.errParametrizacion;
                            sError = $"Retorno[{retval}]";
                        }
                        else
                        {
                            Res = eEjecucion.errParam;
                            sError = $"Retorno[{retval}]";
                        }
                    }
                }
                //WritePerformanceCounters();
            }
            catch (SqlException sql)
            {
                Res = eEjecucion.errParametrizacion;
                sError = $"ErrorSQL No. [{sql.Number}] Mensaje:{sql.Message}";

                if (sql.Number == -2)
                    Res = eEjecucion.errTimeOut;
                else if (sql.Number == 1205)
                    Res = eEjecucion.errDeadLock;
                else if (sql.Number == -1 || sql.Number == 121 || sql.Number == 596)
                    Res = eEjecucion.errConexion;
                else if (sql.Number == 2627)
                    Res = eEjecucion.Repetido;
                else if (sql.Number == 2812)
                    Res = eEjecucion.errVacio;
                else if (sql.Number == 547 || sql.Number == 8114 || sql.Number == 8152)
                    Res = eEjecucion.errNoReint;
            }
            catch (Exception ex)
            {
                Res = eEjecucion.errParametrizacion;
                sError = $"Exception Mensaje: {ex.Message}";
            }
            return Res;
        }
        #endregion

        #region Funciones Auxiliares
        /// ************************************************************************************************
        /// <summary>
        /// Elimina los eventos antiguo de la base de datos local
        /// </summary>
        /// <param name="sTabla">Si es Online o Evento</param>
        /// <param name="daysToDelete">Dias de permanencia</param>
        /// ************************************************************************************************
        private static void BorrarEventosViejos(string sTabla, int daysToDelete)
        {
            _elog.Trace("Entro...");
            string sCampo, sRecords = "", sObservacion = "", sDeletedRec = "";
            bool bResult = false, bResultC = false;
            SqlCommand command = new SqlCommand();

            sCampo = sTabla.Substring(0,3);

            //primero hay que consultar cuantas tablas hay
            command.CommandText = "SELECT COUNT(*) FROM @tabla";
            command.CommandText = command.CommandText.Replace("@tabla", sTabla);
            var task1 = _eve.EjecutaConsulta(_connectionString, command, "CONSULTA").Result;
            bResultC = task1.Item1;
            sRecords = task1.Item2;

            command.CommandText = "DELETE @tabla WHERE DATEDIFF(day,@campo_feven,getdate()) > @dias";
            command.CommandText = command.CommandText.Replace("@tabla", sTabla);
            command.CommandText = command.CommandText.Replace("@campo", sCampo);
            command.Parameters.Add(new SqlParameter { ParameterName = "@dias", Value = daysToDelete, SqlDbType = SqlDbType.Int });

            var task = _eve.EjecutaConsulta(_connectionString, command, "DELETE").Result;
            bResult = task.Item1;
            sDeletedRec = task.Item2;

            if (bResult)
            {
                //si estaba en un reintento establece timer en 100 para volver a calcular tiempo del trigger
                if (_deleteEventsTimer.Interval == 1000)
                {
                    //el enabled se establece en true, se cambia el intervalo, y luego a false para evitar que se dispare el trigger
                    _deleteEventsTimer.Enabled = true;
                    _deleteEventsTimer.Interval = 100;
                    _deleteEventsTimer.Enabled = false;
                }
                //enviar evento de mantenimiento
                sObservacion = $"Se borraron los registros de {sTabla} con mas de {daysToDelete} días de antiguedad.";
                if (bResultC)
                {
                    int nRec, nDeleted, count;
                    int.TryParse(sRecords, out nRec);
                    int.TryParse(sDeletedRec, out nDeleted);
                    if (nRec > 0 && nDeleted > 0)
                    {
                        count = nRec - nDeleted;
                        sObservacion += $" Cantidad de registros antes: {sRecords}, despues: {count}.";
                        GestionComandos.EnviarEventoMantenimiento(sObservacion, 'B');
                    }
                }
                _elog.Info(sObservacion);
            }
            else
            {
                _deleteEventsTimer.Enabled = true;
                _deleteEventsTimer.Interval = 1000;
                _deleteEventsTimer.Enabled = false;
            }

            //borra los archivos .dat, , solo si no estoy reintentando borrar eventos
            if (_deleteEventsTimer.Interval != 1000)
            {
                try
                {
                    DateTime dtFechaCarpeta = DateTime.MinValue, dtNow = DateTime.Now;

                    FileInfo[] listaEve;
                    DirectoryInfo[] listaCarpeta = new DirectoryInfo(_evePath).EnumerateDirectories().ToArray();

                    foreach (DirectoryInfo di in listaCarpeta)
                    {
                        try
                        {
                            dtFechaCarpeta = DateTime.ParseExact(di.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                        }
                        catch (Exception ex)
                        {
                            _elog.Error("Formato fecha erróneo [{0}]", ex.Message);
                            continue;
                        }

                        if (dtFechaCarpeta.Date < dtNow.Date.AddDays(-daysToDelete))
                        {
                            listaEve = di.EnumerateFiles().ToArray();

                            foreach (FileInfo fi in listaEve)
                            {
                                if (fi.Name.Contains(sCampo.ToUpper()))
                                    fi.Delete();
                            }
                        }

                        if (!di.EnumerateFiles().Any())
                            di.Delete();
                    }
                }
                catch (Exception e)
                {
                    _elog.Error("Error al borrar los archivos [{0}]", e.ToString());
                }

                //recupero eventos luego del borrado
                _eve.RecuperarEventos();
            }

            _elog.Trace("Salgo...");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicia el timer de borrado de los eventos
        /// </summary>
        /// ************************************************************************************************
        public static void IniciarBorrado()
        {
            _elog.Trace("Entro..");

            if (_con.Modo == 3 || _con.Modo == 2)
            {
                if(!_estaBorrando)
                {
                    _estaBorrando = true;
                    SetTimerEventDelete();
                }
            }
            _elog.Trace("Salgo..");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Detiene el timer de borrado de los eventos
        /// </summary>
        /// ************************************************************************************************
        public static void DetenerBorrado()
        {
            _elog.Trace("Entro..");
            if (_estaBorrando)
            {
                _deleteEventsTimer.Stop();
                _estaBorrando = false;
            }
            _elog.Trace("Salgo..");
        }

        
        /// ************************************************************************************************
        /// <summary>
        /// Formatea la fecha recibida al formato para almacenar en la BD
        /// </summary>
        /// <param name="sFecha">Fecha a formatear</param>
        /// <returns>Fecha formateada</returns>
        /// ************************************************************************************************
        private string FormatoFechaBd(string sFecha)
        {
            // 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22
            // 2 0 1 8 - 0 3 - 2 0 T  1  3  :  3  0  :  1  6  .  9  2  8
            try
            {
                sFecha = sFecha.Insert(4, "-");
                sFecha = sFecha.Insert(7, "-");
                sFecha = sFecha.Insert(10, " ");
                sFecha = sFecha.Insert(13, ":");
                sFecha = sFecha.Insert(16, ":");
                sFecha = sFecha.Insert(19, ".");
            }
            catch(Exception)
            {
                sFecha = "";
            }

            return sFecha;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Reemplaza los caracteres especiales o extras de los eventos, por unos que permitan su ejecución
        /// en la base de datos de la estación.
        /// </summary>
        /// <param name="sSP">String que contiene el evento</param>
        /// <param name="sTipo">Tipo de evento</param>
        /// <returns>El evento formateado</returns>
        /// ************************************************************************************************
        private string ConvertSP(string sSP, eTipoEventoBD tipo)
        {
            //_elog.Trace("EnvioEventos::ConvertSP -> Entro");

            //No es fuera de secuencia, reemplazo caracteres
            if (tipo != eTipoEventoBD.FueraSecuencia)
                sSP = sSP.Replace("exec ", "exec @RetVal=");

            if (tipo == eTipoEventoBD.FueraSecuencia)
                sSP = sSP.Replace(" 0,", " 1,");

            //_elog.Trace("EnvioEventos::ConvertSP -> Salgo");
            return sSP;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Recupera el mensaje que la Base de Datos imprimió con "PRINT" y lo almacena 
        /// para ciertas validaciones.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// ************************************************************************************************
        static void InfoMessage(object sender, SqlInfoMessageEventArgs e)
        {
            _msg = e.Message;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Detiene la ejecución de los eventos en la Base de Datos de la estación
        /// </summary>
        /// ************************************************************************************************
        public static void DetenerEjecucionEvento()
        {
            _elog.Trace("Entro...");
            _aTimerPrioridad?.Stop();
            _aTimerSinPrioridad?.Stop();
            _elog.Info("Se detiene la ejecución de Eventos...");
            _elog.Trace("Salgo...");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicia la ejecución de los eventos en la Base de Datos de la estación
        /// </summary>
        /// ************************************************************************************************
        public static void IniciarEjecucionEvento()
        {
            _elog.Trace("Entro...");
            _aTimerPrioridad?.Start();
            _aTimerSinPrioridad?.Start();
            _elog.Info("Inicia la ejecución de Eventos...");
            _elog.Trace("Salgo...");
        }
        #endregion

        /// <summary>
        /// Mueve el archivo de evento a la carpeta correspondiente
        /// </summary>
        /// <param name="solicitud"></param>
        /// <param name="ev"></param>
        private void MoverArchivoEvento(bool esViaEscape, SolicitudEnvioEventos solicitud = null, FileInfo ev = null)
        {
            DateTime date;

            if (solicitud != null)
            {
                string viaEst = "";
                if (!esViaEscape)
                    viaEst = _viaEst;
                else
                    viaEst = _viaEscEst;
                //Ejemplo de Nombre EVE_201803201330169283800610410.dat
                string sTipoEvento = (solicitud.Tipo == eTipoEventoBD.Evento ? _tbEveMin : _tbOnlMin).ToUpper();
                string sNombreEve = sTipoEvento + "_" + 
                                    solicitud.FechaGenerado.ToString("yyyyMMddHHmmssfff") +
                                    solicitud.Secuencia.ToString("D5") +
                                    viaEst +
                                    ".dat";

                ev = new FileInfo(Path.Combine(_evePath,sNombreEve));

                //extrae fecha generada
                date = solicitud.FechaGenerado;
            }
            else
            {
                //extrae fecha generada del nombre del archivo
                string[] datos = ev.Name.Split(new Char[] { '_', '.' });

                date = DateTime.MinValue;

                if( datos.Length == 3  && datos[2] == "dat" )
                {
                    string sFecha = FormatoFechaBd( datos[1].Substring( 0, 17 ) );

                    date = DateTime.Parse( sFecha );
                }
            }

            string sPathSaved = string.Empty;

            if( date != DateTime.MinValue )
                sPathSaved = Path.Combine(_evePath, date.ToString("yyyy-MM-dd"));

            try
            {
                if( !string.IsNullOrEmpty(sPathSaved) )
                {
                    //Revisa si existe la carpeta del dia de ese evento
                    if( !Directory.Exists( sPathSaved ) )
                        Directory.CreateDirectory( sPathSaved );

                    if( !File.Exists( Path.Combine( sPathSaved, ev.Name ) ) )
                        File.Move( ev.FullName, Path.Combine( sPathSaved, ev.Name ) ); 
                }
            }
            catch (Exception e)
            {
                _elog.Error("Error al mover archivo {0} al dir {1} Error [{2}]", ev.Name, sPathSaved, e.ToString());
            }
        }

        public DateTime BuscarUltimoEvento()
        {
            DateTime dtFecha = DateTime.MinValue;
            bool bRes;
            string sRes = "";
            SqlCommand command = new SqlCommand();
            command.CommandText = "SELECT TOP 1 @mine_feven FROM @evento ORDER BY @mine_num DESC";
            command.CommandText = command.CommandText.Replace("@evento", _tbEve);
            command.CommandText = command.CommandText.Replace("@mine", _tbEveMin);

            //Ejecuta la consulta
            var task = EjecutaConsulta(_connectionString, command, "CONSULTA").Result;
            bRes = task.Item1;
            sRes = task.Item2;

            if (bRes)
                //Fecha del ultimo evento recibido en la BD local
                dtFecha = DateTime.Parse(sRes);

            return dtFecha;
        }
    }
}