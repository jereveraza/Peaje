using NLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Utiles;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Clase que establece los parámetros de configuración del servicio.
    /// </summary>
    /// ****************************************************************************************************
    public class ConfiguracionBaseDatos
    {
        public string MotorBd { get; set; }
        public string LocalPath { get; set; }
        public string LocalConnection { get; set; }
        public string DatabaseName { get; set; }
        public int PortNumber { get; set; }
        public string ServidorPath { get; set; }
        public string ServidorBd { get; set; }
        public string EventDb { get; set; }
        public string TurnoDb { get; set; }
        public string LogEveDir { get; set; }
        public string EventDir { get; set; }
        public string Retries { get; set; }
        public string EventoPermanencia { get; set; }
        public TimeSpan HoraBorrarEvento { get; set; }
        public int IntervaloRecuperaEventos { get; set; }
        public int MinutosAntiguedadEventos { get; set; }
        public int Modo { get; set; }
        public int MaxDiasEventosRecuperar { get; set; }
        public int MaxDiasEventosFallidos { get; set; }
        public int MinutosReintentoFallidos { get; set; }
        public string UID { get; set; }
        public int Via { get; set; }
        public int Estacion { get; set; }
        public int NumeroViaEscape { get; set; }
        public int DiasEvento { get; set; }
        public int DiasEventoTransito { get; set; }
        public int DiasEventoFactura { get; set; }
        public TimeSpan HoraChequeoDataBD { get; set; }
        public int MaximoMBPorTabla { get; set; }
        public int MaximoRegistrosPorTabla { get; set; }
    }

    public class Configuraciones
    {
        private static Logger _logger = LogManager.GetLogger("logfile");

        private List<string> _listaMensajes = new List<string>();
        private static Lazy<Configuraciones> _instance = new Lazy<Configuraciones>(() => new Configuraciones());
        public static Configuraciones Instance => _instance.Value;

        private static ConfiguracionBaseDatos _configuracionLeida = new ConfiguracionBaseDatos();

        public bool EstadoConfig { get; set; }

        public List<string> Mensaje { get { return _listaMensajes; } set { _listaMensajes = value; } }

        public ConfiguracionBaseDatos Configuracion { get { return _configuracionLeida; } set { _configuracionLeida = value; } }

        private Configuraciones()
        {
            try
            {
                EstadoConfig = true;

                //Configuracion del motor de la BD
                _configuracionLeida.MotorBd = ConfigurationManager.AppSettings["MOTOR_DB"].ToString().ToUpper();

                var last = Enum.GetValues( typeof( MotorBaseDatos ) ).Cast<MotorBaseDatos>().Max();
                for( int i = 0; i < (int)last; i++ )
                {
                    MotorBaseDatos Motor = (MotorBaseDatos)i;
                    if( _configuracionLeida.MotorBd == Motor.ToString() )
                        break;
                    else if( i == (int)last )
                    {
                        EstadoConfig = false;
                        _listaMensajes.Add( "Error en MOTOR_DB: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Incorrecto ) );
                    }
                }

                //Numero de puerto a escuchar
                int nPuerto;
                if (Int32.TryParse(ConfigurationManager.AppSettings["SERVICE_PORT_NUM"].ToString(), out nPuerto))
                    _configuracionLeida.PortNumber = nPuerto;
                else
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en SERVICE_PORT_NUM: " + Utility.ObtenerDescripcionEnum(eErrorConfig.Numerico));
                }

                //Configuracion del servidor local, si no tiene un valor asignado, por defecto quedará en "localhost\SQLEXPRESS"
                string sHostDir = ConfigurationManager.AppSettings["LOCAL_INSTANCE_NAME"].ToString();
                if( string.IsNullOrEmpty( sHostDir ) )
                    sHostDir = "localhost\\SQLEXPRESS";

                //Nombre instancia SQL Local
                _configuracionLeida.LocalPath = sHostDir;

                //Nombre de la BD Local
                _configuracionLeida.DatabaseName = ConfigurationManager.AppSettings["LOCAL_INSTANCE_DB_NAME"].ToString();
                if( string.IsNullOrEmpty( _configuracionLeida.DatabaseName ) )
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en LOCAL_INSTANCE_DB_NAME: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                }

                //Nombre de la BD Local de Turno
                _configuracionLeida.TurnoDb = ConfigurationManager.AppSettings["LOCAL_INSTANCE_TURN_DB_NAME"].ToString();
                if (string.IsNullOrEmpty(_configuracionLeida.TurnoDb))
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en LOCAL_INSTANCE_TURN_DB_NAME: " + Utility.ObtenerDescripcionEnum(eErrorConfig.Vacio));
                }

                //Connection string a la Base de Datos local:
                _configuracionLeida.LocalConnection = "Server=" + _configuracionLeida.LocalPath + ";Database=" + _configuracionLeida.DatabaseName +
                                        ";User Id=sa;Password=TeleAdmin01;Connection Timeout=1";

                //Servidor de la estación
                string sServidorDir = ConfigurationManager.AppSettings["PLAZA_INSTANCE_NAME"].ToString();
                if( string.IsNullOrEmpty( sServidorDir ) )
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en PLAZA_INSTANCE_NAME: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                }

                _configuracionLeida.ServidorPath = sServidorDir;

                //Nombre de la BD de la estación
                _configuracionLeida.ServidorBd = ConfigurationManager.AppSettings["PLAZA_INSTANCE_DB_NAME"].ToString();
                if( string.IsNullOrEmpty( _configuracionLeida.ServidorBd ) )
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en PLAZA_INSTANCE_DB_NAME: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                }

                //Modalidad del servicio
                int nModo;
                if( Int32.TryParse( ConfigurationManager.AppSettings["SERVICE_MODE"].ToString(), out nModo ) )
                    _configuracionLeida.Modo = nModo;
                else
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en SERVICE_MODE: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Numerico ) );
                }

                if( nModo <= 0 || nModo > 3 )
                {
                    EstadoConfig = false;
                    _listaMensajes.Add("Error en SERVICE_MODE: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Rango ) );
                }

                if( nModo == 2 || nModo == 3 )
                {
                    //Nombre de la BD local de los eventos
                    _configuracionLeida.EventDb = ConfigurationManager.AppSettings["LOCAL_INSTANCE_EVENTS_DB_NAME"].ToString();
                    if( string.IsNullOrEmpty( _configuracionLeida.EventDb ) )
                    {
                        EstadoConfig = false;
                        _listaMensajes.Add("Error en LOCAL_INSTANCE_EVENTS_DB_NAME: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                    }

                    //Path en donde serán almacenados los logs de envio de eventos
                    _configuracionLeida.LogEveDir = ConfigurationManager.AppSettings["LOCAL_DIR_EVENT_LOGS"].ToString();
                    if( string.IsNullOrEmpty( _configuracionLeida.LogEveDir ) )
                    {
                        EstadoConfig = false;
                        _listaMensajes.Add("Error en LOCAL_DIR_EVENT_LOGS: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                    }

                    //Path en donde serán almacenados los eventos .Dat
                    _configuracionLeida.EventDir = ConfigurationManager.AppSettings["LOCAL_DIR_EVENT_FILES"].ToString();
                    if( string.IsNullOrEmpty( _configuracionLeida.EventDir ) )
                    {
                        EstadoConfig = false;
                        _listaMensajes.Add("Error en LOCAL_DIR_EVENT_FILES: " + Utility.ObtenerDescripcionEnum( eErrorConfig.Vacio ) );
                    }

                    //Reintentos maximos para la ejecucion de un evento si hay problemas de conexion que no sea timeout
                    _configuracionLeida.Retries = ConfigurationManager.AppSettings["LOCAL_EVENT_EXECUTION_RETRIES"].ToString();
                    if( string.IsNullOrEmpty( _configuracionLeida.Retries ) )
                        _configuracionLeida.Retries = "5";

                    //Dias de permanencia de los eventos
                    _configuracionLeida.EventoPermanencia = ConfigurationManager.AppSettings["LOCAL_EVENT_DAYS_DELETE"].ToString();
                    if( string.IsNullOrEmpty( _configuracionLeida.EventoPermanencia ) )
                        _configuracionLeida.EventoPermanencia = "365";

                    //Horario parra borrar eventos viejos
                    //por defecto debería ser a las 02:00
                    TimeSpan tsHoraBorrar;
                    if (TimeSpan.TryParse(ConfigurationManager.AppSettings["LOCAL_EVENT_DELETE_TIME24"].ToString(), out tsHoraBorrar))
                        _configuracionLeida.HoraBorrarEvento = tsHoraBorrar;
                    else
                        _configuracionLeida.HoraBorrarEvento = new TimeSpan(2, 0, 0);

                    //Minutos de intervalo de timer de recuperacion de eventos
                    int nMinTimerEve;
                    if (int.TryParse(ConfigurationManager.AppSettings["LOCAL_EVENT_CHECK_RECOVERY_MINUTES"].ToString(), out nMinTimerEve))
                        _configuracionLeida.IntervaloRecuperaEventos = nMinTimerEve;
                    else
                        _configuracionLeida.IntervaloRecuperaEventos = 10;

                    //minutos de antiguedad para recuperar eventos en el timer
                    int nMinAntiEve;
                    if (int.TryParse(ConfigurationManager.AppSettings["LOCAL_MAX_RECOVERY_EVENT_MINUTES"].ToString(), out nMinAntiEve))
                        _configuracionLeida.MinutosAntiguedadEventos = nMinAntiEve;
                    else
                        _configuracionLeida.MinutosAntiguedadEventos = 60;


                    //Dias máximos a recuperar eventos al iniciar el Servicio BD
                    int nEveday;
                    if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_MAX_RECOVERY_EVENT_DAYS"].ToString(), out nEveday))
                        _configuracionLeida.MaxDiasEventosRecuperar = nEveday;
                    else
                    {
                        _configuracionLeida.MaxDiasEventosRecuperar = 1;
                    }

                    //Dias máximos a tolerar para ejecutar eventos fallidos
                    if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_MAX_FAILED_EVENT_DAYS"].ToString(), out nEveday))
                        _configuracionLeida.MaxDiasEventosFallidos = nEveday;
                    else
                    {
                        _configuracionLeida.MaxDiasEventosFallidos = 30;
                    }

                    //Minutos para reintentar eventos fallidos
                    int nMinReint;
                    if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_FAILED_EVENT_MINUTES"].ToString(), out nMinReint))
                        _configuracionLeida.MinutosReintentoFallidos = nMinReint;
                    else
                    {
                        _configuracionLeida.MinutosReintentoFallidos = 10;
                    }
                }

                //Dias a considerar para solicitar autorizar numeración:
                //1.Tolerancia de dias de cualquier evento
                int nTolEve;
                if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_OLD_EVENT_DAY"].ToString(), out nTolEve))
                    _configuracionLeida.DiasEvento = nTolEve;
                else
                    _configuracionLeida.DiasEvento = 1;

                //2.Tolerancia de dias de evento de transito
                int nTolTransito;
                if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_OLD_TRANSIT_DAY"].ToString(), out nTolTransito))
                    _configuracionLeida.DiasEventoTransito = nTolTransito;
                else
                    _configuracionLeida.DiasEventoTransito = 1;

                //3.Tolerancia de dias de evento que tenga factura
                int nTolFactura;
                if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_OLD_TICKET_DAY"].ToString(), out nTolFactura))
                    _configuracionLeida.DiasEventoFactura = nTolFactura;
                else
                    _configuracionLeida.DiasEventoFactura = 1;

                //Horario para revisar tamaño y registros de tablas en las BD locales
                //por defecto debería ser a las 00:00
                TimeSpan tsHora;
                if (TimeSpan.TryParse(ConfigurationManager.AppSettings["LOCAL_CHECK_DATA_SIZE_TIME24"].ToString(), out tsHora))
                    _configuracionLeida.HoraChequeoDataBD = tsHora;
                else
                    _configuracionLeida.HoraChequeoDataBD = new TimeSpan(0, 0, 0);


                //Maximo de tamaño en MB de la tabla para incrementar alarma
                int nMaxMB;
                if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_MAX_DATA_SIZE_MB"].ToString(), out nMaxMB))
                    _configuracionLeida.MaximoMBPorTabla = nMaxMB;
                else
                    _configuracionLeida.MaximoMBPorTabla = 500;

                //Maximo de registros en una tabla para incrementar alarma
                int nMaxRows;
                if (Int32.TryParse(ConfigurationManager.AppSettings["LOCAL_MAX_NUM_RECORDS"].ToString(), out nMaxRows))
                    _configuracionLeida.MaximoRegistrosPorTabla = nMaxRows;
                else
                    _configuracionLeida.MaximoRegistrosPorTabla = 1000000; //1 millon
            }
            catch( ConfigurationErrorsException confEx )
            {
                _logger.Error( "Error reading app settings [{0}]", confEx.Message );
                _logger.Debug( confEx.ToString() );
            }
        }

        public void Clear()
        {
            _configuracionLeida.Via = 0;
            _configuracionLeida.Estacion = 0;
            _configuracionLeida.UID = "";
        }
    }
}
