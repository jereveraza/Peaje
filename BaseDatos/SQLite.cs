using System.Data.SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Data;
using Newtonsoft.Json;
using Entidades.ComunicacionBaseDatos;
using Entidades;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Clase que contiene los métodos correspondientes al motor de BD.
    /// </summary>
    /// ****************************************************************************************************
    public class SQLite //: IBaseDatos
    {
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private const int SQLQueryTimeOut = 2;
        private string _connectionString;
        private SQLiteCommand _command;
        private string _consultaSQL;

        /// ************************************************************************************************
        /// <summary>
        /// Establece la string de conexión a la Base de Datos.
        /// </summary>
        /// <param name="config"></param> //La configuración del archivo App.config
        /// ************************************************************************************************
        public SQLite(ConfiguracionBaseDatos config)
        {
            //_connectionString = "Data Source=C:\\" + config.HostDir + "\\" + config.DatabaseName + "; Version = 3; New = False; Compress = True;";
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
                Debug.WriteLine(String.Format(" column string: {0}", rows.ToString()));
            }

            return rows;
        }

        public RespuestaBaseDatos ConsultaBaseEstacion(SolicitudBaseDatos parametros)
        {
            RespuestaBaseDatos result;
            result = new RespuestaBaseDatos();
            return result;
        }
        /// ************************************************************************************************
        /// <summary>
        /// Realiza la conexión a la Base de Datos.
        /// Ejecuta la acción indicada por la solicitud del cliente.
        /// </summary>
        /// <param name="parametros"></param>
        /// <returns>La respuesta de la Base de datos a la solicitud recibida del cliente</returns>
        /// ************************************************************************************************
        public RespuestaBaseDatos ConsultaBaseLocal(SolicitudBaseDatos parametros)
        {
            string strResult = String.Empty;
            RespuestaBaseDatos result = new RespuestaBaseDatos();

            if (parametros == null)
            {
                //Error: El objeto recibido es nulo
                result.CodError = (EnmErrorBaseDatos.ErrorFormatoIn);
                result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorFormatoIn);
            }
            else
            {
                //Armo la consulta SQLite
                if (parametros.Accion == eAccionBD.Consulta)
                {
                    _consultaSQL = "SELECT * FROM " + Utility.ObtenerDescripcionEnum(parametros.Tabla);
                    if (parametros.Filtro != "")
                    { 
                        _consultaSQL = _consultaSQL + " WHERE " + parametros.Filtro;
                    }
                }
                else if (parametros.Accion == eAccionBD.Update)
                {
                    string[] campos = parametros.Filtro.Split(new Char[] { '|' });
                    _consultaSQL = "UPDATE " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " SET " + campos[0] + " WHERE " + campos[1];
                }
                else if (parametros.Accion == eAccionBD.Insert)
                {
                    _consultaSQL = "INSERT INTO " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " VALUES (" + parametros.Filtro + ")";
                }
                else if (parametros.Accion == eAccionBD.Delete)
                {
                    _consultaSQL = "DELETE FROM " + Utility.ObtenerDescripcionEnum(parametros.Tabla) + " WHERE " + parametros.Filtro;
                }

                using (SQLiteConnection _connection = new SQLiteConnection(_connectionString))
                {
                    result.RespuestaDB = String.Empty;

                    try
                    {
                        _command = new SQLiteCommand();
                        _command.Connection = _connection;
                        _command.CommandTimeout = SQLQueryTimeOut;
                        //Envio el string de consulta sql
                        _command.CommandText = _consultaSQL;
                        //Me conecto y abro el puerto
                        _command.Connection.Open();
                        Debug.WriteLine("-> SQLite command string: " + _consultaSQL);
                        //Si es consulta leo los datos que me responde la BD
                        if (parametros.Accion == eAccionBD.Consulta)
                        {
                            SQLiteDataReader _reader = _command.ExecuteReader();

                            if (_reader != null)
                            {
                                var rows = ConvertToDictionary(_reader);
                                result.RespuestaDB = JsonConvert.SerializeObject(rows, Formatting.None);
                                _reader.Close();

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
                                _command.ExecuteNonQuery();

                                //no hay error
                                result.CodError = EnmErrorBaseDatos.SinFalla;
                            }
                            catch (SQLiteException e)
                            {
                                _logger.Debug("SQLite:Instruccion() Excepcion [{0}:{1}]", e.ErrorCode, e.Message);
                                Debug.WriteLine("SQLite:Instruccion() Excepcion [{0}:{1}]", e.ErrorCode, e.Message);
                                result.CodError = Exceptions(e.ErrorCode,e.Message.ToString());
                            }
                        }
                        else
                        {
                            //Error: accion incorrecta
                            result.CodError = EnmErrorBaseDatos.ErrorAccion;
                        }

                        result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                    }
                    catch (SQLiteException e)
                    {
                        _logger.Debug("SQLite:Instruccion() Excepcion [{0}:{1}]", e.ErrorCode,e.Message);

                        //Busco el error
                        result.CodError = Exceptions(e.ErrorCode,e.Message.ToString());
                        result.DescError = Utility.ObtenerDescripcionEnum((EnmErrorBaseDatos)result.CodError);
                        return result;
                    }
                    catch (Exception e)
                    {
                        _logger.Debug("SQLite:Instruccion() Otra Excepcion [{0}]", e.Message);
                        //Error: otros errores
                        result.CodError = EnmErrorBaseDatos.ErrorBaseDatos;
                        result.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ErrorBaseDatos);
                        return result;
                    }
                    finally
                    {
                        //Cierro la conexion
                        _command.Connection.Close();
                    }
                }
            }
            return result;
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
            string sTabla = "no such table";
            string sColum = "no such column";

            if (sError.Contains(sTabla))
                error = EnmErrorBaseDatos.ErrorTabla;
            else if (sError.Contains(sColum))
                error = EnmErrorBaseDatos.ErrorFiltro;
            else
            {
                switch (eNumer)
                {
                    case 1:
                        {
                            error = EnmErrorBaseDatos.SintaxisIncorrecta;
                            break;
                        }
                    case 2:
                    case 5:
                    case 8:
                    case 11:
                    case 13:
                    case 14:
                        {
                            error = EnmErrorBaseDatos.ErrorBaseDatos;
                            break;
                        }
                    case 3:
                        {
                            error = EnmErrorBaseDatos.ErrorBDLogin;
                            break;
                        }
                    case 6:
                        {
                            error = EnmErrorBaseDatos.ErrorTabla;
                            break;
                        }
                    case 20:
                        {
                            error = EnmErrorBaseDatos.ErrorFiltro;
                            break;
                        }
                    default:
                        {
                            //Error: otros errores
                            error = EnmErrorBaseDatos.ErrorBaseDatos;
                            break;
                        }
                }
            }
            return error;
        }
    }   
}
