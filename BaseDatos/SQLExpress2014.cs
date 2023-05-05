using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Newtonsoft.Json;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Entidades;
using Entidades.ComunicacionBaseDatos;
using Utiles;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Clase que contiene los métodos correspondientes al motor de BD.
    /// </summary>
    /// ****************************************************************************************************
    public class SQLExpress2014 //: IBaseDatos
    {
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private int SQLQueryTimeOut = 5;
        private string _connectionString, _connectionStringEst, _tbVars;
        private int _via, _estacion;
        private static bool _existeVars = false, _existeConfig = false;
        private const string TABLA_CONFIGURACION = "Configuracion";
        public string Connection { get { return _connectionString; } set { _connectionString = value; } }

        /// ************************************************************************************************
        /// <summary>
        /// Establece la string de conexión a la Base de Datos.
        /// </summary>
        /// <param name="config"></param> //La configuración del archivo App.config
        /// ************************************************************************************************
        public SQLExpress2014(ConfiguracionBaseDatos config)
        {
            _connectionString = $"Server={config.LocalPath};Database={config.DatabaseName}" +
                                ";User Id=sa;Password=TeleAdmin01;Connection Timeout=1";
        }

        /// <summary>
        /// Obtiene configuracion de la via
        /// </summary>
        /// <param name="config"></param>
        public void Init(ConfiguracionBaseDatos config)
        {
            string sPasswordDB = Utility.GetHashString(config.UID.ToUpper());
            _connectionStringEst = $"Server={config.ServidorPath};Database={config.ServidorBd};User Id={config.UID};Password={sPasswordDB};Connection Timeout=1";

            _tbVars = eTablaBD.Vars.ToString() + config.UID.Substring(3, 3);
            _via = config.Via;
            _estacion = config.Estacion;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Agrega a una lista lo que va leyendo de la BD, guardandolo por columna y fila
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        /// ************************************************************************************************
        private IEnumerable<Dictionary<string, object>> ConvertToDictionary(IDataReader reader)
        {
            var columns = new List<string>();
            var rows = new List<Dictionary<string, object>>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            while (reader.Read())
            {
                rows.Add(columns.ToDictionary(column => column, column => reader[column]));
            }

            return rows;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Realiza la conexión a la Base de Datos.
        /// Ejecuta la acción indicada por la solicitud del cliente.
        /// </summary>
        /// <param name="parametros"></param>
        /// <returns>La respuesta de la Base de datos a la solicitud recibida del cliente</returns>
        /// ************************************************************************************************
        public async Task<RespuestaBaseDatos> ConsultaBaseLocal(SolicitudBaseDatos parametros)
        {
            string sConex = _connectionString, sConsulta = string.Empty; ;
            RespuestaBaseDatos result = new RespuestaBaseDatos();
            SQLQueryTimeOut = 2;

            if (parametros == null)
            {
                //Error: El objeto recibido es nulo
                result.CodError = (EnmErrorBaseDatos.ErrorFormatoIn);
                result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorFormatoIn);
            }
            else
            {
                if (!string.IsNullOrEmpty(parametros.Filtro))
                    parametros.Filtro = Utility.DeserializarFiltro(parametros.Filtro);

                //La vía solicita informacion
                if (parametros.Accion == eAccionBD.Consulta)
                {
                    sConsulta = ProcesarConsulta(parametros);
                    if (sConsulta == "Error")
                    {
                        result.CodError = EnmErrorBaseDatos.ErrorFiltro;
                        result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorFiltro);
                        return result;
                    }
                }
                else if (parametros.Accion == eAccionBD.Update)
                {
                    string[] campos = parametros.Filtro.Split(new Char[] { '|' });
                    sConsulta = "UPDATE " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " SET " + campos[0] + " WHERE " + campos[1];
                }
                else if (parametros.Accion == eAccionBD.Insert)
                {
                    if(parametros.Tabla == eTablaBD.Vars)
                        sConsulta = "INSERT INTO " + _tbVars + " VALUES (" + parametros.Filtro + ")";
                    else
                        sConsulta = "INSERT INTO " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " VALUES (" + parametros.Filtro + ")";
                }
                else if (parametros.Accion == eAccionBD.Delete)
                {
                    sConsulta = "DELETE FROM " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " WHERE " + parametros.Filtro;
                }

                try
                {
                    using (SqlConnection connection = new SqlConnection(sConex))
                    {
                        result.RespuestaDB = String.Empty;

                        //Establezco la conexión...
                        connection.Open();
                        SqlCommand command = new SqlCommand(sConsulta, connection);
                        command.CommandTimeout = SQLQueryTimeOut;

                        //Si es consulta leo los datos que me responde la BD
                        if (parametros.Accion == eAccionBD.Consulta)
                        {
                            //Si se quiere realizar un SELECT a VARS primero chequea que exista, de lo contrario la crea
                            if (parametros.Tabla == eTablaBD.Vars && !_existeVars)
                            {
                                string sDim = _tbVars.Substring(0, 3).ToLower();
                                string sAux = $"IF OBJECT_ID('{_tbVars}', 'U') IS NULL CREATE TABLE {_tbVars} ({sDim}_ID int IDENTITY(1,1) PRIMARY KEY, {sDim}_veh varchar(max) null)";
                                command.CommandText = sAux;

                                try
                                {
                                    await command.ExecuteNonQueryAsync();
                                    command.CommandText = sConsulta;
                                    _existeVars = true;
                                }
                                catch (SqlException e)
                                {
                                    _logger.Debug($"Excepcion U/I/D Al crear {_tbVars}.. Consulta realizada: {sAux}. [{e.Number}:{e.Message}]");
                                }
                            }

                            SqlDataReader reader = await command.ExecuteReaderAsync();

                            if (reader != null)
                            {
                                var rows = ConvertToDictionary(reader);
                                if(rows.Any())
                                    result.RespuestaDB = JsonConvert.SerializeObject(rows, Formatting.None);

                                reader.Close();

                                if (result.RespuestaDB == "")
                                {
                                    //Error: No se encontro la busqueda
                                    result.CodError = EnmErrorBaseDatos.SinResultado;
                                }
                                else
                                {
                                    //Busqueda correcta, no hay error
                                    result.CodError = EnmErrorBaseDatos.SinFalla;
                                }
                            }
                            else
                            {
                                //Algo salio mal con el reader, indico error
                                result.CodError = EnmErrorBaseDatos.ErrorLecturaBD;
                            }
                        }
                        else if (parametros.Accion == eAccionBD.Update || parametros.Accion == eAccionBD.Insert || parametros.Accion == eAccionBD.Delete)
                        {
                            try
                            {
                                command.ExecuteNonQuery();

                                //no hay error
                                result.CodError = EnmErrorBaseDatos.SinFalla;
                            }
                            catch (SqlException e)
                            {
                                _logger.Debug("Excepcion U/I/D. Consulta Realizada: {0} [{1}:{2}]", sConsulta, e.Number, e.Message);
                                result.CodError = Exceptions(e.Number, "");
                            }
                        }
                        else
                        {
                            //Error: accion incorrecta
                            result.CodError = EnmErrorBaseDatos.ErrorAccion;
                        }

                        result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                    }
                }
                catch (SqlException e)
                {
                    _logger.Debug($"SQL Excepcion. Consulta Realizada: {sConsulta}. [{e.Number}:{e.Message}]");
                    //Busco el error
                    result.CodError = Exceptions(e.Number, "");
                    result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                    return result;
                }
                catch(InvalidOperationException e)
                {
                    _logger.Debug($"InvalidOperation. Consulta Realizada: {sConsulta}. [{e.Message}]");
                    //Busco el error
                    result.CodError = EnmErrorBaseDatos.ErrorBaseDatos;
                    result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorBaseDatos);
                    return result;
                }
                catch(Exception e)
                {
                    _logger.Debug($"Otras Excepciones. Consulta realizada: {sConsulta} [{e.Message}]");
                    //Error: otros errores
                    result.CodError = EnmErrorBaseDatos.ErrorBaseDatos;
                    result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorBaseDatos);
                    return result;
                }
            }
            return result;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Realiza la conexión a la Base de Datos de la estación.
        /// Ejecuta la acción indicada por la solicitud del cliente.
        /// </summary>
        /// <param name="parametros">Parametros a evaluar</param>
        /// <returns>Respuesta a la vía</returns>
        /// ************************************************************************************************
        public async Task<RespuestaBaseDatos> ConsultaBaseEstacion(SolicitudBaseDatos parametros)
        {
            string sConsulta = String.Empty;
            RespuestaBaseDatos result = new RespuestaBaseDatos();
            SQLQueryTimeOut = 2;

            if (parametros == null)
            {
                //Error: El objeto recibido es nulo
                result.CodError = (EnmErrorBaseDatos.ErrorFormatoIn);
                result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorFormatoIn);
            }
            else
            {
                //Armo la consulta SQL
                if (parametros.Accion == eAccionBD.Procedure)
                {
                    string sProcedure;

                    switch (parametros.Tabla)
                    {
                        case eTablaBD.Comandos:
                            {
                                sProcedure = "ViaNet.usp_GetComandos";
                                break;
                            }
                        case eTablaBD.GetParte:
                            {
                                sProcedure = "ViaNet.usp_GetParte";
                                break;
                            }
                        case eTablaBD.GetUltimoTurno:
                            {
                                sProcedure = "ViaNet.usp_GetUltTurnos";
                                break;
                            }
                        case eTablaBD.GetServerDate:
                            {
                                sProcedure = "ViaNet.usp_GetFechahora";
                                break;
                            }
                        case eTablaBD.ConfiguracionDeVia:
                            {
                                sProcedure = "ViaNet.usp_GetConfViaNew";
                                break;
                            }
                        case eTablaBD.ListaDeTags:
                            {
                                sProcedure = "ViaNet.usp_GetTagsNew";
                                break;
                            }
                        case eTablaBD.Operador:
                            {
                                sProcedure = "ViaNet.usp_GetOperadoresNew";
                                break;
                            }
                        case eTablaBD.Tarifa:
                            {
                                sProcedure = "ViaNet.usp_GetTarifasNew";
                                break;
                            }
                        case eTablaBD.MensajesDetraccion:
                            {
                                sProcedure = "ViaNet.usp_GetMensajesDetraccion";
                                break;
                            }
                        default:
                            {
                                sProcedure = "";
                                break;
                            }
                    }

                    if (string.IsNullOrEmpty(parametros.Filtro))
                        sConsulta = $"EXEC {sProcedure} {_estacion},{_via}";
                    else if (parametros.Tabla != eTablaBD.Comandos)
                    {
                        parametros.Filtro = Utility.DeserializarFiltro(parametros.Filtro);
                        sConsulta = $"EXEC {sProcedure} {_estacion},{_via},{parametros.Filtro}";
                    }
                    else if (parametros.Tabla == eTablaBD.Comandos)
                        sConsulta = $"EXEC {sProcedure} {_estacion},{_via},{parametros.Filtro}";
                }

                try
                {
                    using (SqlConnection connection = new SqlConnection(_connectionStringEst))
                    {
                        result.RespuestaDB = String.Empty;

                        //Establezco la conexión...
                        connection.Open();
                        SqlCommand command = new SqlCommand(sConsulta, connection);
                        command.CommandTimeout = SQLQueryTimeOut;
                            
                        //Si es consulta leo los datos que me responde la BD
                        if (parametros.Accion == eAccionBD.Procedure)
                        {
                            SqlDataReader reader = await command.ExecuteReaderAsync();

                            if (reader != null)
                            {
                                var rows = ConvertToDictionary(reader);
                                result.RespuestaDB = JsonConvert.SerializeObject(rows, Formatting.None);
                                reader.Close();

                                if (result.RespuestaDB == "")
                                {
                                    //Error: No se encontro la busqueda
                                    result.CodError = EnmErrorBaseDatos.SinResultado;
                                }
                                else
                                {
                                    //Busqueda correcta, no hay error
                                    result.CodError = EnmErrorBaseDatos.SinFalla;
                                }
                            }
                            else
                            {
                                //Algo salio mal con el reader, indico error
                                result.CodError = EnmErrorBaseDatos.ErrorLecturaBD;
                            }
                        }
                        else
                        {
                            //Error: accion incorrecta
                            result.CodError = EnmErrorBaseDatos.ErrorAccion;
                        }

                        result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                    }
                }
                catch (SqlException e)
                {
                    _logger.Debug($"SQL Excepcion. Consulta Realizada: {sConsulta}. [{e.Number}:{e.Message}]");
                    //Busco el error
                    result.CodError = Exceptions(e.Number, "");
                    result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                    return result;
                }
                catch (Exception e)
                {
                    _logger.Debug($"Otras Excepciones. Consulta realizada: {sConsulta} [{e.Message}]");
                    //Error: otros errores
                    result.CodError = (EnmErrorBaseDatos.ErrorBaseDatos);
                    result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorBaseDatos);
                    return result;
                }
            }
            return result;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Procesa la consulta recibida
        /// </summary>
        /// <param name="consulta">La solicitud recibida</param>
        /// <returns>El query correspondiente</returns>
        /// ************************************************************************************************
        public string ProcesarConsulta(SolicitudBaseDatos consulta)
        {
            string sRetConsulta = string.Empty;
            string sDim = consulta.Tabla.ToString().Substring(0, 3);
            string sTabla = Utility.ObtenerDescripcionEnum(consulta.Tabla);

            sRetConsulta = "SET ARITHABORT ON;\n ";

            if (consulta.Expresion == eExpresionBD.Join)
            {
                if (string.IsNullOrEmpty(consulta.Filtro))
                    return "Error";

                if (consulta.Tabla == eTablaBD.ListaDeTags)
                {
                    string clave = consulta.Filtro.Substring(consulta.Filtro.IndexOf('=') + 1);
                    string campo = consulta.Filtro.Substring(0, consulta.Filtro.IndexOf('='));

                    sRetConsulta += $"DECLARE @clave varchar(200) " +
                                    $"SET @clave = {clave} " +
                                    $"SELECT {sTabla}.*, ISNULL(pre_saldo,tag_saldo) Saldo, Accion.*, {Utility.ObtenerDescripcionEnum(eTablaBD.MediosDePago)}.* from {sTabla} " +
                                    $"LEFT JOIN Accion ON lam_codig = tag_status " +
                                    $"LEFT JOIN Medios_Pago ON lam_tipcu = CFG_TIPCU " +
                                    $"AND tag_subfp = CFG_SUBFP AND(tag_tipo = CFG_TIPOP OR CFG_TIPOP is null)" +
                                    $"LEFT JOIN Saldos ON tag_numcl = pre_numcl " +
                                    $"WHERE ({campo} = @clave)" +
                                    $"OPTION (OPTIMIZE FOR (@clave = {clave}))";
                }
            }
            else
            {
                //Caso especial: contar registros de la tabla especificada
                if (consulta.Expresion == eExpresionBD.Count)
                    sRetConsulta += "SELECT COUNT(*) AS " + consulta.Filtro + " FROM " + sTabla;
                //Caso especial: ordenar la tabla y tomar el último registro
                else if (consulta.Expresion == eExpresionBD.Top)
                {
                    string sAux = consulta.Tabla == eTablaBD.Vars ? _tbVars : sTabla;
                    sRetConsulta += $"SELECT TOP {consulta.LimiteRegistros} * FROM {sAux}";

                }
                else if (consulta.Expresion == eExpresionBD.Default)
                    sRetConsulta += $"SELECT * FROM {sTabla}";

                //Si tiene algo en filtro, significa que hay que buscar con este
                //Si no tiene filtro la consulta devuelve todos los registros de la tabla
                if (!string.IsNullOrEmpty(consulta.Filtro))
                    sRetConsulta += $" WHERE {consulta.Filtro}";

                if (consulta.Orden != eOrdenBD.Default)
                {
                    sRetConsulta += $" ORDER BY {consulta.CamposOrden} {consulta.Orden}";
                }
            }

            return sRetConsulta;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Clasifica la excepción producida para informar al cliente de forma mas sencilla.
        /// </summary>
        /// <param name="eNumer"></param> Numero de la excepción producida
        /// <param name="sError"></param> Descripción del error.
        /// <returns>El enumerado correspondiente a la categoría del error</returns>
        /// ************************************************************************************************
        public EnmErrorBaseDatos Exceptions(int eNumer, string sError)
        {
            EnmErrorBaseDatos error = new EnmErrorBaseDatos();
            switch (eNumer)
            {
                case 156:
                case 208:
                    {
                        //Error: tabla inexistente
                        error = EnmErrorBaseDatos.ErrorTabla;
                        break;
                    }
                case 105:
                case 207:
                case 213:
                case 245:
                case 4145:
                case 8115:
                case 10774:
                    {
                        //Error: sintaxis del filtro
                        error = EnmErrorBaseDatos.ErrorFiltro;
                        break;
                    }
                case 18456:
                case 18452:
                case 4060:
                    {
                        //Error: login incorrecto en la base
                        error = EnmErrorBaseDatos.ErrorBDLogin;
                        break;
                    }
                case 102:
                case 8180:
                    {
                        error = EnmErrorBaseDatos.SintaxisIncorrecta;
                        break;
                    }
                case 7357:
                    {
                        error = EnmErrorBaseDatos.ProcedureVacio;
                        break;
                    }
                case 2812:
                    {
                        error = EnmErrorBaseDatos.ProcedureInexistente;
                        break;
                    }
                default:
                    {
                        //Error: otros errores
                        error = EnmErrorBaseDatos.ErrorBaseDatos;
                        break;
                    }
            }
            return error;
        }

        public bool BuscarConfiguracion(string sOpcion)
        {
            bool bRet = false;
            string sConsulta = "";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                //Establezco la conexión...
                try
                {
                    connection.Open();
                    SqlCommand command = new SqlCommand(sConsulta, connection);
                    command.CommandTimeout = SQLQueryTimeOut;

                    if (!_existeConfig)
                    {
                        sConsulta = $"IF OBJECT_ID('{TABLA_CONFIGURACION}', 'U') IS NULL CREATE TABLE {TABLA_CONFIGURACION} " +
                                    $"(Via int NOT NULL, Est int NOT NULL, UID varchar(20) NOT NULL)";
                        command.CommandText = sConsulta;
                        command.ExecuteNonQuery();

                        _existeConfig = true;
                    }

                    if (sOpcion == "BUSCAR")
                    {
                        sConsulta = "SELECT * FROM " + TABLA_CONFIGURACION;
                        command.CommandText = sConsulta;

                        SqlDataReader reader = command.ExecuteReader();
                        if (reader != null)
                        {
                            while (reader.Read())
                            {
                                Configuraciones.Instance.Configuracion.Via = int.Parse(reader["Via"].ToString());
                                Configuraciones.Instance.Configuracion.Estacion = int.Parse(reader["Est"].ToString());
                                Configuraciones.Instance.Configuracion.UID = reader["UID"].ToString();

                                bRet = true;
                            }

                            reader.Close();
                        }

                        if (bRet)
                            Init(Configuraciones.Instance.Configuracion);
                    }
                    else if (sOpcion == "GUARDAR")
                    {
                        sConsulta = $"IF NOT EXISTS (SELECT * FROM {TABLA_CONFIGURACION}) INSERT INTO {TABLA_CONFIGURACION} " +
                                    $"VALUES ({Configuraciones.Instance.Configuracion.Via},{Configuraciones.Instance.Configuracion.Estacion}, " +
                                    $"'{Configuraciones.Instance.Configuracion.UID}') ELSE UPDATE {TABLA_CONFIGURACION} " +
                                    $"SET Via = {Configuraciones.Instance.Configuracion.Via}, " +
                                    $"Est = {Configuraciones.Instance.Configuracion.Estacion}, " +
                                    $"UID = '{Configuraciones.Instance.Configuracion.UID}'";
                        command.CommandText = sConsulta;
                        command.ExecuteNonQuery();

                        Init(Configuraciones.Instance.Configuracion);
                    }
                    else if (sOpcion == "CONFIGVIA")
                    {
                        sConsulta = "SELECT * FROM " +  ClassUtiles.GetEnumDescr(eTablaBD.ConfiguracionDeVia);
                        command.CommandText = sConsulta;

                        SqlDataReader reader = command.ExecuteReader();
                        if (reader != null)
                        {
                            while (reader.Read())
                            {
                                bRet = true;
                            }

                            reader.Close();
                        }
                    }
                }
                catch (SqlException ex)
                {
                    _logger.Debug("EXCEPTION {0}", ex.Message);
                }
                catch (Exception e)
                {
                    _logger.Debug("EXCEPTION {0}", e.Message);
                }
            }

            return bRet;
        }
    }
}
