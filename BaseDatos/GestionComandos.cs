using Entidades;
using Entidades.ComunicacionBaseDatos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModuloBaseDatos
{
    public class GestionComandos
    {
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private ConfigListas _conList = null;
        private static int _via, _est, _modo;
        private static string _connectionString;
        private static EnvioEventos _envioEve = new EnvioEventos();
        private Utility _util = new Utility();
        #region Constantes
        private const string format = "yyyyMMdd HH:mm:ss";
        private const string SPFALLACRI = "ViaNet.usp_setFallasCriticas";
        private const string SPENCENDIDO = "ViaNet.usp_setApagadoEncendido";
        private const string SPUPDATECOM = "ViaNet.usp_UpdateComandos";
        private const string tablaSupervCon = "Superv_Conn";
        #endregion

        public bool NoRedSent { get; set; }
        public string SupervConn { get; set; }
        public bool NoFallaSent { get; set; }

        /// ****************************************************************************************************
        /// <summary>
        /// Constructor, obtiene configuracion del servicio
        /// </summary>
        /// <param name="con"></param>
        /// ****************************************************************************************************
        public GestionComandos(ConfigListas listas)
        {
            _logger.Trace("Entro...");
            _connectionString = Configuraciones.Instance.Configuracion.LocalConnection;
            _via = Configuraciones.Instance.Configuracion.Via;
            _est = Configuraciones.Instance.Configuracion.Estacion;
            _modo = Configuraciones.Instance.Configuracion.Modo;
            NoRedSent = false;
            SupervConn = "";
            NoFallaSent = false;
            _conList = listas;
            _logger.Trace("Salgo...");
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Procesa el comando al ejecutar Getcomandos
        /// </summary>
        /// <param name="resComando">Resultado de la ejecucion de GetComandos</param>
        /// ****************************************************************************************************
        public void ProcesarComando(RespuestaBaseDatos resComando)
        {
            List<Comandos> lista = null;
            //agrego a una lista los posibles comandos
            try
            {
                lista = JsonConvert.DeserializeObject<List<Comandos>>(resComando.RespuestaDB);
            }
            catch (JsonException e)
            {
                _logger.Error("JsonExcepcion [{0}]", e.ToString());
                return;
            }
            //Si no hay nada me salgo
            if (lista.Any())
                _logger.Info("Hay comandos de supervisión");
            else
                return;

            //recorro lista para evaluar cada comando
            foreach (Comandos com in lista)
            {
                if (InterpretarComando(com))
                    _logger.Info("El comando {0} se ejecutó correctamente.", com.Codigo);
                else
                    _logger.Info("El comando {0} no pudo ser ejecutado.", com.Codigo);
            }
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Interpreta el comando recibido
        /// </summary>
        /// <param name="oComandos">Comando a interpretar</param>
        /// <returns>True si lo pudo ejecutar de lo contrario False</returns>
        /// ****************************************************************************************************
        private bool InterpretarComando(Comandos oComandos)
        {
            bool bRet = false, bEnvia = true;

            oComandos.Codigo = string.IsNullOrEmpty(oComandos.Codigo) ? oComandos.Codigo : oComandos.Codigo.Trim();
            oComandos.Parametro = string.IsNullOrEmpty(oComandos.Parametro) ? oComandos.Parametro : oComandos.Parametro.Trim();
            _logger.Info("Llegó comando: Via[{0}],Codigo[{1}],Parametro[{2}],Usuario[{3}]",oComandos.NumeroVia,oComandos.Codigo,oComandos.Parametro,oComandos.UsuarioSolicitante);
            //Evitar procesar cualquier comando que no sea el de conexión
            if (oComandos.Codigo != "CONN" && SupervConn == "N")
                return bRet;

            //Si es modo 2 solo le informo a la vía que llegaron comandos
            if(_modo != 2)
            {
                bool esParaViaEscape = false;
                if (Configuraciones.Instance.Configuracion.NumeroViaEscape != 0 && (oComandos.NumeroVia == Configuraciones.Instance.Configuracion.NumeroViaEscape))
                    esParaViaEscape = true;

                //Es un comando de configuración
                if (oComandos.Codigo == "CONF")
                {
                    string sObservacion = "";
                    eStatComando status;
                    bEnvia = false;

                    if (!esParaViaEscape)
                    {
                        //Viene mas de una lista
                        if (oComandos.Parametro.Contains("|"))
                        {
                            string[] listas = oComandos.Parametro.Split(new Char[] { '|' });

                            foreach (string li in listas)
                            {
                                var task = Task.Run(async () => await _conList.ActualizaPorComando(li));
                                bRet = task.Result.Item2;
                            }
                        }
                        //Solo una lista
                        else
                        {
                            var task = Task.Run(async () => await _conList.ActualizaPorComando(oComandos.Parametro));
                            bRet = task.Result.Item2;
                        }

                        if (bRet)
                        {
                            sObservacion = "Consulta realizada";
                            status = eStatComando.Ejecutado;
                        }
                        else
                        {
                            sObservacion = "Consulta Falló";
                            status = eStatComando.NoEjecutado;
                        }
                    }
                    else  //Comando para via de escape
                    {
                        sObservacion = "No se puede ejecutar en vía de escape";
                        status = eStatComando.NoEjecutado;
                    }

                    //Envio evento de update
                    EnviarEventoUpdateComando(oComandos.ID, status, sObservacion, esParaViaEscape);
                }
                //Comando de Mantenimiento
                else if (oComandos.Codigo == "CONN")
                {
                    RespuestaBaseDatos respuesta = null;
                    string sObservacion = "";
                    eStatComando status;

                    if (!esParaViaEscape)
                    {
                        //Conectar
                        if (oComandos.Parametro == "C")
                        {
                            if (SupervConn == "N")
                            {
                                _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, eEstadoRed.Ambas.ToString());
                                SupervConn = "S";
                                Consulta("UPDATE", SupervConn);

                                EnvioEventos.IniciarEjecucionEvento();

                                respuesta = EnviarFallaCritica(EnmFallaCritica.FCSiRed);

                                if (respuesta.CodError == EnmErrorBaseDatos.SinFalla)
                                    bRet = true;

                                sObservacion = "Conexión Iniciada";
                                status = eStatComando.Ejecutado;
                            }
                            else
                            {
                                sObservacion = "Via ya estaba conectada";
                                status = eStatComando.NoEjecutado;
                            }

                            EnviarEventoUpdateComando(oComandos.ID, status, sObservacion, esParaViaEscape);
                        }
                        //Desconectar
                        if (oComandos.Parametro == "D")
                        {
                            _util.EnviarNotificacion(EnmErrorBaseDatos.EstadoConexion, eEstadoRed.SoloLocal.ToString());
                            SupervConn = "N";
                            Consulta("UPDATE", SupervConn);

                            if (!NoRedSent)
                            {
                                NoRedSent = true;

                                respuesta = EnviarFallaCritica(EnmFallaCritica.FCNoRed);

                                if (respuesta.CodError == EnmErrorBaseDatos.SinFalla)
                                    bRet = true;
                            }

                            EnviarEventoUpdateComando(oComandos.ID, eStatComando.Ejecutado, "Desconexión Iniciada", esParaViaEscape);

                            //Le doy tiempo para que se ejecute...
                            Thread.Sleep(1000);

                            EnvioEventos.DetenerEjecucionEvento();
                        }
                    }
                    else  //Comando para via de escape
                    {
                        sObservacion = "No se puede ejecutar en vía de escape";
                        status = eStatComando.NoEjecutado;//Envio evento de update
                        EnviarEventoUpdateComando(oComandos.ID, status, sObservacion, esParaViaEscape);
                    }
                }
                else if(oComandos.Codigo == "APAG")
                {
                    if (!esParaViaEscape)
                    {
                        _logger.Info("Se apagará la PC de vía...");
                        var psi = new ProcessStartInfo("shutdown", "/s /t 0");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;

                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.Ejecutado, "Comando Apagar OK", esParaViaEscape);
                        bEnvia = false;
                        _util.EnviarNotificacion(EnmErrorBaseDatos.ComandoSupervEjecutado, "", oComandos);

                        //Le doy tiempo para que se ejecute...
                        Thread.Sleep(5000);

                        Process.Start(psi);
                        bRet = true;
                    }
                    else  //Comando para via de escape
                    {
                        //Envio evento de update
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.NoEjecutado, "No se puede ejecutar en vía de escape", esParaViaEscape);
                    }
                }
                else if(oComandos.Codigo == "REIN")
                {
                    if (!esParaViaEscape)
                    {
                        _logger.Info("Se reiniciará la PC de vía...");
                        var psi = new ProcessStartInfo("shutdown", "/r /t 0");
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;

                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.Ejecutado, "Comando Reinicio OK", esParaViaEscape);
                        bEnvia = false;
                        _util.EnviarNotificacion(EnmErrorBaseDatos.ComandoSupervEjecutado, "", oComandos);

                        //Le doy tiempo para que se ejecute...
                        Thread.Sleep(5000);

                        Process.Start(psi);
                        bRet = true;
                    }
                    else  //Comando para via de escape
                    {
                        //Envio evento de update
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.NoEjecutado, "No se puede ejecutar en vía de escape", esParaViaEscape);
                    }
                }
                else if(oComandos.Codigo == "REVE")
                {
                    SolicitudEnvioEventos solicitud = new SolicitudEnvioEventos();
                    RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
                    string[] fechas = oComandos.Parametro.Split(new Char[] { '|' });
                    string sDesde, sHasta;

                    sDesde = fechas[0];
                    sHasta = fechas[1];

                    _logger.Info("Se recuperarán los eventos desde {0} hasta {1} via [{2}]",sDesde,sHasta,oComandos.NumeroVia);

                    //Armo solicitud
                    solicitud.AccionEvento = eAccionEventoBD.Recuperar;
                    solicitud.FechasRecuperacion = $"{sDesde}|{sHasta}";

                    var task = Task.Run(async () => await _envioEve.SolicitudCliente(solicitud, esParaViaEscape));
                    respuesta = task.Result;

                    if(respuesta.CodError == EnmErrorBaseDatos.Recuperacion)
                    {
                        bRet = true;
                        //Envio el evento
                        EnviarEventoMantenimiento(respuesta.RespuestaDB, 'P');
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.Ejecutado, "Comando Recupera Evento OK", esParaViaEscape);
                    }
                    else
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.NoEjecutado, "Comando Recupera Evento Fallo", esParaViaEscape);
                }
                else if (oComandos.Codigo == "REFA")
                {
                    SolicitudEnvioEventos solicitud = new SolicitudEnvioEventos();
                    RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
                    string sDesde = oComandos.Parametro;

                    _logger.Info("Se recuperarán los eventos fallidos desde {0} - Via[{1}]", sDesde, oComandos.NumeroVia);

                    //Armo solicitud
                    solicitud.AccionEvento = eAccionEventoBD.Recuperar;
                    solicitud.FechasRecuperacion = sDesde;

                    var task = Task.Run(async () => await _envioEve.SolicitudCliente(solicitud, esParaViaEscape));
                    respuesta = task.Result;

                    if (respuesta.CodError == EnmErrorBaseDatos.SinFalla)
                    {
                        bRet = true;
                        //Envio el evento
                        EnviarEventoMantenimiento(respuesta.RespuestaDB, 'P');
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.Ejecutado, "Comando Recupera Evento Fallido OK", esParaViaEscape);
                    }
                    else
                        EnviarEventoUpdateComando(oComandos.ID, eStatComando.NoEjecutado, "Comando Recupera Evento Fallido Falla", esParaViaEscape);
                }
                else
                {
                    _util.EnviarNotificacion(EnmErrorBaseDatos.ComandoSupervRecibido, "", oComandos);
                    bEnvia = false;
                }
                    
                if (bEnvia)
                    _util.EnviarNotificacion(EnmErrorBaseDatos.ComandoSupervEjecutado, "", oComandos);
            }
            else
                _util.EnviarNotificacion(EnmErrorBaseDatos.ComandoSupervRecibido, "", oComandos);

            return bRet;
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Arma el evento de falla crítica y lo guarda
        /// </summary>
        /// <param name="eFalla">Estado de la red</param>
        /// <param name="sObservacion">Observación de la falla</param>
        /// <returns>El resultado de la operacion</returns>
        /// ****************************************************************************************************
        private RespuestaBaseDatos EnviarFallaCritica(EnmFallaCritica eFalla, string sObservacion = "")
        {
            SolicitudEnvioEventos solicitud = new SolicitudEnvioEventos();
            RespuestaBaseDatos respuesta = null;
            string sEvento = string.Empty, sTurno = string.Empty;
            DateTime dtHoraEvento = new SqlDateTime(DateTime.Now).Value;

            int nTurno = GestionTurno.ObtenerNumeroTurno();

            //Armo el evento
            sEvento = "exec ";
            sEvento += SPFALLACRI;
            sEvento += $" 0,{_est},{_via},'{dtHoraEvento.ToString(format)}',{(int)eFalla},'{sObservacion}',{nTurno},null";

            //Armo la solicitud
            solicitud.AccionEvento = eAccionEventoBD.Guardar;
            solicitud.Tipo = eTipoEventoBD.Evento;
            solicitud.FechaGenerado = dtHoraEvento;
            solicitud.SqlString = sEvento;
            solicitud.Secuencia = 0;

            Utility.GuardarEventoEnArchivo(solicitud.Secuencia, sEvento, dtHoraEvento);

            var task = Task.Run(async () => await _envioEve.SolicitudCliente(solicitud, false));
            respuesta = task.Result;

            if (respuesta.CodError == EnmErrorBaseDatos.EventoNoAlmacenado)
                _logger.Info("No se almacenó el evento de FallaCritica...");

            return respuesta;
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Envia el evento de los eventos recuperados
        /// </summary>
        /// <param name="sObservacion">Observacion para el evento de mantenimiento</param>
        /// <returns>El resultado de la operacion</returns>
        /// ****************************************************************************************************
        public static RespuestaBaseDatos EnviarEventoMantenimiento(string sObservacion, char sMovim)
        {
            SolicitudEnvioEventos solicitud = new SolicitudEnvioEventos();
            RespuestaBaseDatos respuesta = null;
            string sEvento = string.Empty;
            DateTime dtHoraEvento = new SqlDateTime(DateTime.Now).Value;

            sEvento = "exec ";
            sEvento += SPENCENDIDO;
            sEvento += $" 0,{_est},{_via},'{dtHoraEvento.ToString(format)}','{sMovim}','{sObservacion}','',0,''";

            //Armo la solicitud
            solicitud.AccionEvento = eAccionEventoBD.Guardar;
            solicitud.Tipo = eTipoEventoBD.Evento;
            solicitud.FechaGenerado = dtHoraEvento;
            solicitud.SqlString = sEvento;
            solicitud.Secuencia = 0;

            Utility.GuardarEventoEnArchivo(solicitud.Secuencia, sEvento, dtHoraEvento);

            var task = Task.Run(async () => await _envioEve.SolicitudCliente(solicitud, false));
            respuesta = task.Result;

            if (respuesta.CodError == EnmErrorBaseDatos.EventoNoAlmacenado)
                _logger.Info("No se almacenó el evento de Mantenimiento...");

            return respuesta;
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Envia el evento de Update Comando
        /// </summary>
        /// <param name="nID">ID del comando</param>
        /// <param name="status">Estatus del comando</param>
        /// <param name="sObservacion">Observación asociada al comando</param>
        /// <returns>El resultado de la operacion</returns>
        /// ****************************************************************************************************
        private RespuestaBaseDatos EnviarEventoUpdateComando(int nID, eStatComando status, string sObservacion, bool esViaEscape)
        {
            SolicitudEnvioEventos solicitud = new SolicitudEnvioEventos();
            RespuestaBaseDatos respuesta = null;
            string sEvento = string.Empty;
            DateTime dtHoraEvento = new SqlDateTime(DateTime.Now).Value;
            int nuvia;

            if (!esViaEscape)
                nuvia = _via;
            else
                nuvia = Configuraciones.Instance.Configuracion.NumeroViaEscape;

            sEvento = "exec ";
            sEvento += SPUPDATECOM;
            sEvento += $" {0},{_est},{nuvia},'{dtHoraEvento.ToString(format)}',{nID},'{Utility.ObtenerDescripcionEnum(status)}','{sObservacion}'";

            //Armo la solicitud
            solicitud.AccionEvento = eAccionEventoBD.Guardar;
            solicitud.Tipo = eTipoEventoBD.Evento;
            solicitud.FechaGenerado = dtHoraEvento;
            solicitud.SqlString = sEvento;
            solicitud.Secuencia = 0;

            Utility.GuardarEventoEnArchivo(solicitud.Secuencia, sEvento, dtHoraEvento);

            var task = Task.Run(async () => await _envioEve.SolicitudCliente(solicitud, esViaEscape));
            respuesta = task.Result;

            if (respuesta.CodError == EnmErrorBaseDatos.EventoNoAlmacenado)
                _logger.Info("No se almacenó el evento de Updatecomandos...");

            return respuesta;
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Permite utilizar el envio de evento de falla crítica
        /// </summary>
        /// <param name="eFalla">Falla crítica</param>
        /// <param name="observacion">Observacion de la falla</param>
        /// <returns>El resultado de la operacion</returns>
        /// ****************************************************************************************************
        public RespuestaBaseDatos EnviarEvento(EnmFallaCritica eFalla, string observacion)
        {
            if (NoFallaSent)
                return new RespuestaBaseDatos();
            NoFallaSent = true;
            return EnviarFallaCritica(eFalla, observacion);
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Permite consultar el estado de conexión según comando de Supervisión
        /// </summary>
        /// <param name="sAccion">Accion a realizar</param>
        /// <param name="sAux">Parametro auxiliar para las consultas</param>
        /// <returns>True si realizó la consulta, de lo contrario False</returns>
        /// ****************************************************************************************************
        public bool ConsultaTablaEstado(string sAccion, string sAux = "")
        {
            return Consulta(sAccion, sAux);
        }

        /// ****************************************************************************************************
        /// <summary>
        /// Realiza consultas a la BD local
        /// </summary>
        /// <param name="sAccion">Accion a realizar</param>
        /// <param name="sAux">Parametro auxiliar para las consultas</param>
        /// <returns>True si realizó la consulta, de lo contrario False</returns>
        /// ****************************************************************************************************
        private bool Consulta(string sAccion, string sAux = "")
        {
            string sConsultaSQL = string.Empty;
            bool bRet = false;
            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand())
            {
                try
                {
                    command.Connection = connection;
                    command.CommandTimeout = 2;
                    connection.Open();
                    //Creo la tablaSupervConn si aun no está creada
                    if (sAccion == "CHECK")
                    {
                        sConsultaSQL = $"IF OBJECT_ID('{tablaSupervCon}', 'U') IS NULL CREATE TABLE {tablaSupervCon}" +
                                        " (sup_stat char(1) not null)";

                        try
                        {
                            command.CommandText = sConsultaSQL;
                            command.ExecuteNonQuery();
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("SQL Excepcion verificando tablaSupervCon. [{0}:{1}]", e.Number, e.Message);
                            return bRet;
                        }

                        bRet = true;
                    }
                    //Consulta tablaSupervConn para obtener el status de la conexión segun el ultimo comando de conn/descon
                    else if (sAccion == "CONSULTA")
                    {
                        sConsultaSQL = "SELECT * FROM " + tablaSupervCon;
                        command.CommandText = sConsultaSQL;
                        bool bNoRow = false;

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader != null)
                            {
                                if (reader.Read())
                                    SupervConn = reader["sup_stat"].ToString();
                                else
                                    bNoRow = true;
                            }
                            else
                            {
                                //Algo salio mal con el reader, indico error
                                _logger.Error("Reader vacío.");
                                return bRet;
                            }
                        }

                        //Si se realiza la consulta y el reader no leyó nada es porque no hay filas, inserto una con valor ""
                        if (bNoRow)
                        {
                            sConsultaSQL = "INSERT INTO " + tablaSupervCon + " VALUES ('" + SupervConn + "')";
                            command.CommandText = sConsultaSQL;

                            try
                            {
                                command.ExecuteNonQuery();
                            }
                            catch (SqlException e)
                            {
                                _logger.Error("ExcepcionSQL. {0}:{1}", e.Number, e.Message);
                                return bRet;
                            }
                        }

                        bRet = true;
                    }
                    //Realiza UPDATE al estado de la conexion segun el ultimo comando de conn/descon
                    else if (sAccion == "UPDATE")
                    {
                        sConsultaSQL = $"UPDATE {tablaSupervCon} SET sup_stat = '{sAux}'";
                        command.CommandText = sConsultaSQL;

                        try
                        {
                            command.ExecuteNonQuery();
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("ExcepcionSQL. {0}:{1}", e.Number, e.Message);
                            return bRet;
                        }
                        bRet = true;
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error("SQL Excepcion. {0}:{1}", e.Number, e.Message);
                    return bRet;
                }
                catch (Exception e)
                {
                    _logger.Error("Otras Excepciones. {0}", e.ToString());
                    return bRet;
                }
            }
            return bRet;
        }
    }
}