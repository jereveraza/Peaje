using Entidades;
using Entidades.ComunicacionBaseDatos;
using ModuloBaseDatos.Entidades;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ModuloBaseDatos
{
    public class Utility
    {
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private AsynchronousSocketListener _socket = new AsynchronousSocketListener();
        private static string _colTabla = "Tabla", _colRegistro = "Registros", _colTamano = "SizeMB";

        /// ************************************************************************************************
        /// <summary>
        /// Arma la notificacion a enviar al cliente
        /// </summary>
        /// <param name="error">Error resultante</param>
        /// <param name="sInfo">Informacion adicional</param>
        /// <param name="oComandos">Comandos si se recibió comando de sueprvisión</param>
        /// ************************************************************************************************
        public void EnviarNotificacion(EnmErrorBaseDatos error, string sInfo, Comandos oComandos = null)
        {
            RespuestaBaseDatos notificacion = new RespuestaBaseDatos();
            notificacion.CodError = error;
            notificacion.DescError = ObtenerDescripcionEnum(error);

            //es un comando que debo informar a la vía
            if(sInfo == "" && oComandos != null)
            {
                string sComando = JsonConvert.SerializeObject(oComandos);
                notificacion.RespuestaDB = sComando;
            }
            //es una alerta de conexión o actualizacion
            else
                notificacion.RespuestaDB = sInfo;

            try
            {
                string sNotificacion = JsonConvert.SerializeObject(notificacion);
                _socket.EnviaNotificacionCliente(sNotificacion);
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion al serializar Json de notificación. {0}", e.Message);
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Obtiene la descripción de un enumerado
        /// </summary>
        /// <param name="enume">Enumerado a evaluar</param>
        /// <returns>Descripción del enumerado</returns>
        /// ************************************************************************************************
        public static string ObtenerDescripcionEnum(Enum enume)
        {
            string descripcion = string.Empty;
            DescriptionAttribute da;

            FieldInfo fi = enume.GetType().GetField(enume.ToString());
            da = (DescriptionAttribute)Attribute.GetCustomAttribute(fi, typeof(DescriptionAttribute));

            if (da != null)
                descripcion = da.Description;
            else
                descripcion = enume.ToString();

            return descripcion;
        }

        public static string DeserializarFiltro(string sFiltro)
        {
            //Se obtiene el objeto para deserializar
            try
            {
                var json = JObject.Parse(sFiltro);
                string sClase = (string)json.GetValue("Objeto");
                Type objConsulta = Type.GetType(sClase);

                if (!string.IsNullOrEmpty(sFiltro))
                {
                    var jObj = JsonConvert.DeserializeObject(sFiltro, objConsulta);
                    sFiltro = jObj.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Excepcion al obtener el filtro de la consulta: {0}", ex.Message);
            }

            return sFiltro;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Envía a otro método el UID para que lo interprete y devuelva la clave de acceso a la BD de la
        /// estación.
        /// </summary>
        /// <param name="inputString"></param>//El UID de App.config
        /// <returns>Retorna la clave específica para el UID proporcionado en App.config</returns>
        /// ************************************************************************************************
        public static string GetHashString(string inputString)
        {
            return Convert.ToBase64String(GetHash(inputString));
        }

        /// ************************************************************************************************
        /// <summary>
        /// Genera la clave de conexión a la BD de estación mediante el uso del UID proporcionado en config.
        /// </summary>
        /// <param name="inputString"></param>//El UID de App.config
        /// <returns>Retorna la clave específica para el UID proporcionado en App.config</returns>
        /// ************************************************************************************************
        private static byte[] GetHash(string inputString)
        {
            byte[] key = { 0xE3, 0x8A, 0xD2, 0x14, 0x94, 0x3D, 0xAA, 0xD1, 0xD6, 0x4C, 0x10, 0x2F, 0xAE, 0x02, 0x9D, 0xE4, 0xAF, 0xE9, 0xDA, 0x3D };

            HMACSHA1 SHA1 = new HMACSHA1(key);

            HashAlgorithm algorithm = SHA1;
            return algorithm.ComputeHash(Encoding.ASCII.GetBytes(inputString));
        }

        public static bool VerificarTablasBase(string sConnection, bool bInicio, ref List<DBData> lTablas)
        {
            bool bRet = false;

            using (SqlConnection connection = new SqlConnection(sConnection))
            using (SqlCommand command = new SqlCommand())
            {
                try
                {
                    command.Connection = connection;
                    //Establezco la conexión...
                    connection.Open();
                    if (bInicio)
                    {
                        command.CommandText = $@"SELECT 
                                            {_colTabla} = DB_NAME(database_id),
                                            {_colTamano} = CAST(SUM(size) * 8. / 1024 AS DECIMAL(8,2))
                                            FROM sys.master_files WITH(NOWAIT)
                                            WHERE database_id = DB_ID()
                                            GROUP BY database_id";
                    }
                    else
                    {
                        command.CommandText = $@"SELECT 
                                        t.Name AS {_colTabla},
                                        p.rows AS {_colRegistro},
                                        CAST(ROUND((SUM(a.used_pages) / 128.00), 2) AS NUMERIC(36, 2)) AS {_colTamano}
                                        FROM sys.tables t
                                        INNER JOIN sys.indexes i ON t.OBJECT_ID = i.object_id
                                        INNER JOIN sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
                                        INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                                        GROUP BY t.Name, p.Rows
                                        ORDER BY t.Name";
                    }
                    command.CommandTimeout = 5;

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader != null)
                        {
                            while (reader.Read())
                            {
                                DBData odbData = new DBData();
                                int nReg = 0;
                                double dSize;
                                odbData.ColTabla = reader[_colTabla].ToString();
                                if (!bInicio)
                                    odbData.ColRegistro = Int32.TryParse(reader[_colRegistro].ToString(), out nReg) ? nReg : 0;
                                odbData.ColTamaño = Double.TryParse(reader[_colTamano].ToString(), out dSize) ? dSize : 0;

                                if (nReg > Configuraciones.Instance.Configuracion.MaximoRegistrosPorTabla ||
                                    dSize > Configuraciones.Instance.Configuracion.MaximoMBPorTabla || bInicio)
                                {
                                    lTablas.Add(odbData);
                                    bRet = true;
                                }
                            }
                        }
                    }
                }
                catch (SqlException ex)
                {
                    _logger.Error("EXCEPTION {0}", ex.Message);
                }
                catch (Exception e)
                {
                    _logger.Error("EXCEPTION {0}", e.ToString());
                }
            }

            return bRet;
        }

        private static object _fileLock = new object();
        /// <summary>
        /// Agrego método de GuardarEventoenArchivo de ModuloEventos(lógica)
        /// </summary>
        /// <param name="Secuencia"></param>
        /// <param name="SqlString"></param>
        /// <param name="dtFecha"></param>
        public static void GuardarEventoEnArchivo(int Secuencia, string SqlString, DateTime dtFecha)
        {
            try
            {
                string fileName;
                lock (_fileLock)
                {
                    fileName = "EVE_";
                    int nuvia = Configuraciones.Instance.Configuracion.Via;
                    fileName += dtFecha.ToString("yyyyMMddHHmmssfff") + Secuencia.ToString("D5") +
                                nuvia.ToString("D3") + Configuraciones.Instance.Configuracion.Estacion.ToString("D2") + ".dat";
                    using (FileStream fs = File.Create(Path.Combine(Configuraciones.Instance.Configuracion.EventDir, fileName)))
                    using (StreamWriter outputFile = new StreamWriter(fs))
                    {
                        outputFile.WriteLine(SqlString);
                    }
                }
            }
            catch (Exception e)
            {
                _logger?.Error(e);
            }
        }

        /// <summary>
        /// Convierte estado de un evento al enumerado
        /// </summary>
        /// <param name="sEstado"></param>
        /// <returns></returns>
        public static eEstado EstadoEvento(string sEstado)
        {
            eEstado estado = eEstado.PENDIENTE;
            bool bParse = false;

            bParse = Enum.TryParse(sEstado, out estado);

            //si no lo pudo convertir correctamente revisamos uno por uno
            if (!bParse)
            {
                if (sEstado == eEstado.PENDIENTE.ToString())
                    estado = eEstado.PENDIENTE;
                else if (sEstado == eEstado.ENVIADO.ToString())
                    estado = eEstado.ENVIADO;
                else if (sEstado == eEstado.FALLA_SP.ToString())
                    estado = eEstado.FALLA_SP;
                else if (sEstado == eEstado.FALLA_PARAM.ToString())
                    estado = eEstado.FALLA_PARAM;
                else if (sEstado == eEstado.RECHAZADO.ToString())
                    estado = eEstado.RECHAZADO;
                else if (sEstado == eEstado.REPETIDO.ToString())
                    estado = eEstado.REPETIDO;
                else if (sEstado == eEstado.RECUPERADO.ToString())
                    estado = eEstado.RECUPERADO;
            }

            return estado;
        }
    }
}
