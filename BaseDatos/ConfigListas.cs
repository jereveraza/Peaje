using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using Entidades.ComunicacionBaseDatos;
using Entidades;
using ModuloBaseDatos.Entidades;

namespace ModuloBaseDatos
{
    public class ConfigListas
    {
        #region Private
        private ActualizacionDB _actualiza = new ActualizacionDB();
        private DateTime _dtUltima, dtActual;
        private string _connectionString, _versionSql;
        private ConfiguracionBaseDatos _con = new ConfiguracionBaseDatos();
        private List<CamposLista> _cLista = new List<CamposLista>();
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private Utility _util = new Utility();
        #endregion

        #region Constant
        private const int SQLQueryTimeOut = 2, MAXIMO_REGISTROS = 10000;
        private const string TABLA_CONFIG_LISTA = "Config_Lista", COLUMNA_FILTRO_LISTA = "lis_codig", TABLA_UPDATE_LISTA = "Lista_Tiempo", NOMBRE_TABLA_SP = "Lista_Sp", NOMBRE_SP_LISTAS = "ViaNet.usp_GetStoredProcedures";
        #endregion

        #region Construccion
        /// ************************************************************************************************
        /// <summary>
        /// Establece la string de conexión a la Base de Datos.
        /// Toma la configuración y extrae los valores necesarios.
        /// </summary>
        /// <param name="config"></param> //La configuración del archivo App.config
        /// ************************************************************************************************
        public ConfigListas()
        {
            _logger.Trace("Entro...");
            _connectionString = Configuraciones.Instance.Configuracion.LocalConnection;
            _logger.Trace("Salgo...");
        }

        public void Init()
        {
            //Obtiene configuracion
            _con = Configuraciones.Instance.Configuracion;

            //Inicio
            _actualiza.Init();
        }
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Busca si es tiempo de actualizar alguna lista.
        /// </summary>
        /// ************************************************************************************************
        public void BuscarActualizaciones()
        {
            _logger.Trace("Entro...");
            bool res = false, bAux = false;

            _logger.Debug("Entro a buscar actualizaciones");

            //Obtiene todos los tipos de listas posibles que existen para el cliente
            if (Consultas("GETLISTAS"))
            {
                //recorre los tipos de lista y evalua si le toca actualizar a cada una...
                foreach(CamposLista lista in _cLista)
                {
                    //si se habia enviado un comando de supervision de DESCONECTAR me salgo porque no debería actualizar
                    //nada en ese estado de conexion
                    if (AsynchronousSocketListener.GetSupervConn() == "N")
                    {
                        _logger.Info("No se pudo actualizar el Tipo de Lista : {0} " +
                                     "debido a que se envió el comando de Desconexión desde Supervisión", lista);
                        continue;
                    }

                    //Busca si a el tipo de lista le toca actualizar
                    if(Actualiza(lista))
                    {
                        _logger.Info($"Comienza actualización de listas {lista.Codigo}");
                        int nFallidas = 0;

                        //Envía a actualizar..
                        var task = Task.Run(async () => await _actualiza.ActualizaPorTipoLista(lista.Codigo));
                        bAux = task.Result.Item1;
                        nFallidas = task.Result.Item2;
                        EnmErrorBaseDatos estadoLista;

                        //si todo OK
                        if(bAux)
                        {
                            //hace update a la tabla auxiliar que contiene la ultima vez que se actualizo esa lista.
                            res = Consultas("UPDATE",lista.Codigo);
                            if (!res)
                                _logger.Warn("No se pudo actualizar la tabla {0}", TABLA_UPDATE_LISTA);

                            _logger.Info($"Se Actualizaron la lista {lista.Codigo}");

                            estadoLista = EnmErrorBaseDatos.ListaActualizada;

                            if (nFallidas > 0)
                                _logger.Debug($"Pero quedaron pendientes por actualizar {nFallidas} lista(s)");
                        }
                        else
                        {
                            _logger.Info("Ocurrió un error actualizando la lista {0}", lista.Codigo);
                            estadoLista = EnmErrorBaseDatos.ListaNoActualizada;
                        }

                        _util.EnviarNotificacion(estadoLista, lista.Codigo);
                    }
                }
            }
            else
                _logger.Warn("No se pudieron obtener los tipos de listas");

            _logger.Trace("Salgo...");
        }

        /// ************************************************************************************************
        /// <summary>
        /// Consulta si el tipo de lista buscado debe ser actualizado
        /// </summary>
        /// <param name="chTlista">El tipo de lista a evaluar</param>
        /// <returns>True si a ese tipo de lista le toca ser actualizada, de lo contrario false</returns>
        /// ************************************************************************************************
        private bool Actualiza(CamposLista oLista)
        {
            _logger.Trace("Entro...");
            bool res, bRet=false;
            TimeSpan tsAux;
            int nCohor,nDiferencia;
            dtActual = DateTime.Now;

            res = Consultas("CONSULTA", oLista.Codigo);

            if (res)
            {
                Int32.TryParse(oLista.Frecuencia, out nCohor);

                switch (oLista.Modo)
                {
                    //Cada n segundos
                    case "H":
                        {
                            //La hora de consulta es la hora actual mas la frecuencia
                            tsAux = TimeSpan.FromSeconds(nCohor * 60);
                            DateTime dtConsulta = _dtUltima + tsAux;

                            if (dtActual >= dtConsulta)
                                bRet = true; //Si le toca consultar

                            break;
                }
                    //Horario determinado por día
                    case "D":
                        {
                            int nSeg = 0, nMin = 0, nHora = 0;

                            Int32.TryParse(oLista.Horario.Substring(0, 2), out nHora);
                            Int32.TryParse(oLista.Horario.Substring(3), out nMin);
                            Int32.TryParse(oLista.Diferencia, out nDiferencia);

                            DateTime dtConsulta = new DateTime(dtActual.Year,dtActual.Month,dtActual.Day,nHora,nMin,0);

                             nSeg = nDiferencia * (_con.Via % 100);
                            if(nSeg > 59)
                            {
                                nMin = (nSeg / 60);
                                nSeg -= (nMin * 60);
                            }

                            tsAux = new TimeSpan(0, 0, nMin, nSeg);
                            dtConsulta += tsAux;
                            if (_dtUltima < dtConsulta && dtActual >= dtConsulta)
                                bRet = true;

                            break;
                        }
                }
            }
            else
                _logger.Warn("Hubo un problema al consultar la tabla {0}", TABLA_CONFIG_LISTA);

            _logger.Trace("Salgo...");
            return bRet;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Consulta en la base de datos la modalidad de actualización de la lista, y también la última vez
        /// que se actualizó, si no hay última vez agrega el registro o lo modifica en tabla auxiliar.
        /// </summary>
        /// <param name="sAccion">Qué desea realizar?</param>
        /// <param name="sCodigoLista">Codigo de lista a actualizar</param>
        /// <returns>True si le toca actualizar, False en caso contrario</returns>
        /// ************************************************************************************************
        private bool Consultas(string sAccion, string sCodigoLista = "")
        {
            _logger.Trace("Entro...");
            bool bRet = false;
            string sConsultaSQL = string.Empty;

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();

                    if (sAccion == "CONSULTA")
                    {
                        //Verifica si la TABLA_UPDATE_LISTA (tabla auxiliar) existe o no, si no existe la crea
                        sConsultaSQL = "IF OBJECT_ID('" + TABLA_UPDATE_LISTA + "', 'U') IS NULL CREATE TABLE " + TABLA_UPDATE_LISTA +
                                        " (lis_codig char(1) not null, lis_ultima varchar(20) null)";

                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
                            command.CommandText = sConsultaSQL;
                            try
                            {
                                command.CommandText = sConsultaSQL;
                                command.ExecuteNonQuery();
                            }
                            catch (SqlException e)
                            {
                                _logger.Error("SQL Excepcion verificando TABLA_UPDATE_LISTA. [{0}:{1}]", e.Number, e.Message);
                                return bRet;
                            }
                        }

                        //Consulta última vez de actualización
                        sConsultaSQL = "SELECT lis_ultima from " + TABLA_UPDATE_LISTA + " WHERE " + COLUMNA_FILTRO_LISTA + " = '" + sCodigoLista + "'";
                        bool bRead = false;
                        //command.CommandText = sConsultaSQL;
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
                            command.CommandText = sConsultaSQL;
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader != null)
                                {
                                    //Hay registro de última vez
                                    if (reader.Read())
                                    {
                                        bRead = true;
                                        try
                                        {
                                            _dtUltima = DateTime.Parse(reader["lis_ultima"].ToString());
                                        }
                                        catch (FormatException)
                                        {
                                            _dtUltima = DateTime.MinValue;
                                        }
                                    }
                                }
                                else
                                {
                                    //Algo salio mal con el reader, indico error
                                    _logger.Error("Reader vacío.");
                                    return bRet;
                                }
                            }

                            if(!bRead)
                            {
                                //Inserta registro de última vez
                                sConsultaSQL = "INSERT INTO " + TABLA_UPDATE_LISTA + " VALUES ('" + sCodigoLista + "',null)";
                                command.CommandText = sConsultaSQL;

                                try
                                {
                                    command.ExecuteNonQuery();
                                }
                                catch (SqlException e)
                                {
                                    _logger.Error("ExcepcionSQL. {0}:{1}", e.Number, e.Message);
                                }
                            }
                        }

                        bRet = true;
                    }
                    //Realiza UPDATE a última consulta de lista
                    else if(sAccion == "UPDATE")
                    {
                        sConsultaSQL = "UPDATE " + TABLA_UPDATE_LISTA + " SET lis_ultima = '" + dtActual + "' WHERE " + COLUMNA_FILTRO_LISTA + "='" + sCodigoLista + "'";
                        //command.CommandText = sConsultaSQL;
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
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
                    //Obtiene todos los tipos de listas
                    else if(sAccion == "GETLISTAS")
                    {
                        sConsultaSQL = $"SELECT * FROM {TABLA_CONFIG_LISTA}";
                        //command.CommandText = sConsultaSQL;
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
                            command.CommandText = sConsultaSQL;
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader != null)
                                {
                                    while (reader.Read())
                                    {
                                        CamposLista cl = new CamposLista();
                                        cl.Codigo = reader["lis_codig"].ToString();
                                        cl.Modo = reader["lis_modo"].ToString();
                                        cl.Frecuencia = reader["lis_cohor"].ToString();
                                        cl.Horario = reader["lis_codia"].ToString();
                                        cl.Diferencia = reader["lis_midif"].ToString();
                                        _cLista.Add(cl);
                                    }
                                }
                                else
                                {
                                    //Algo salio mal con el reader, indico error
                                    _logger.Error("Reader vacío");
                                    return bRet;
                                }
                            }
                        }
                        bRet = true;
                    }
                    //Obtiene la version SQL del servidor de la estacion
                    else if(sAccion == "VERSION")
                    {
                        sConsultaSQL = $"SELECT * FROM OPENQUERY([{_con.ServidorPath}],'SELECT @@VERSION AS ''SQLVersion''')";
                        //command.CommandText = sConsultaSQL;
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
                            command.CommandText = sConsultaSQL;
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader != null)
                                {
                                    while (reader.Read())
                                    {
                                        _versionSql += reader[0].ToString();
                                    }
                                }
                                else
                                {
                                    //Algo salio mal con el reader, indico error
                                    _logger.Error("Reader vacío. ");
                                    return bRet;
                                }
                            }
                        }
                        bRet = true;
                    }
                    else if (sAccion == "TABLAS")
                    {
                        string sListas = string.Empty, sTablas = string.Empty;
                        //Obtiene el total de tablas en la BD
                        sConsultaSQL = $"SELECT COUNT(*) FROM sys.objects WHERE type_desc = 'USER_TABLE'";
                        //command.CommandText = sConsultaSQL;
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = connection;
                            command.CommandTimeout = SQLQueryTimeOut;
                            command.CommandText = sConsultaSQL;
                            SqlDataReader reader = command.ExecuteReader();
                            if (reader != null)
                            {
                                if (reader.Read())
                                {
                                    sTablas = reader[0].ToString();
                                    _logger.Info("Numero de tablas creadas en la BD: {0}...", sTablas);
                                }
                                reader.Close();

                                //Obtiene el total de listas 
                                sConsultaSQL = $"SELECT COUNT(*) from {NOMBRE_TABLA_SP}";

                                command.CommandText = sConsultaSQL;
                                reader = command.ExecuteReader();

                                if (reader != null)
                                {
                                    if (reader.Read())
                                    {
                                        sListas = reader[0].ToString();
                                        _logger.Debug("Numero de listas: {0}...", sListas);
                                    }
                                    reader.Close();
                                }

                            }
                            else
                            {
                                //Algo salio mal con el reader, indico error
                                _logger.Error("Reader vacío.");
                                return bRet;
                            }
                        }

                        if (!string.IsNullOrEmpty(sTablas) && !string.IsNullOrEmpty(sListas))
                        {
                            int nTablas = 0, nListas = 0;
                            if (int.TryParse(sTablas, out nTablas) && Int32.TryParse(sListas, out nListas))
                            {
                                bRet = nTablas > nListas && nListas > 0 ? true : false;
                                _logger.Info("La BD {0} cargó todas las listas",bRet? "SI":"NO");
                            }
                        }
                        else
                            _logger.Warn("No se pudo obtener el numero de Tablas o Listas...");
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
            _logger.Trace("Salgo...");
            return bRet;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Actualiza tipo de lista según comando enviado desde la vía
        /// </summary>
        /// <param name="sLista">Comando</param>
        /// <returns>True si actualizó la lista, de lo contrario false</returns>
        /// ************************************************************************************************
        public async Task<Tuple<RespuestaBaseDatos,bool>> ActualizaPorComando(string sLista)
        {
            _logger.Trace("Entro...");
            _logger.Debug("Voy a actualizar: {0}",sLista);
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            EnmErrorBaseDatos estadoLista;
            bool bRet = false;
            int nFalla = 0;

            //Envía a actualizar
            var tuple = await _actualiza.ActualizaPorTipoLista(sLista);
            bRet = tuple.Item1;
            nFalla = tuple.Item2;

            if (string.IsNullOrEmpty(sLista))
                sLista = eTablaBD.INIT.ToString();

            //Armo respuesta en función de resultado
            if (bRet)
            {
                respuesta.CodError = EnmErrorBaseDatos.ComandoEjecutado;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ComandoEjecutado);
                respuesta.RespuestaDB = "Actualizacion Finalizada: " + sLista;
                estadoLista = EnmErrorBaseDatos.ListaActualizada;

                //hace update a la tabla auxiliar que contiene la ultima vez que se actualizo esa lista.
                Consultas("UPDATE", sLista);

                _logger.Debug("Se actualizaron las listas por comando: {0}", sLista);
            }
            else
            {
                respuesta.CodError = EnmErrorBaseDatos.ComandoNoEjecutado;
                respuesta.DescError = Utility.ObtenerDescripcionEnum(EnmErrorBaseDatos.ComandoNoEjecutado);
                respuesta.RespuestaDB = "Actualizacion NO Finalizó: " + sLista;
                estadoLista = EnmErrorBaseDatos.ListaActualizada;
                _logger.Debug("Ocurrió un error actualizando las listas por comando: {0}", sLista);
            }

            _util.EnviarNotificacion(estadoLista, sLista);

            _logger.Trace("Salgo...");

            return new Tuple<RespuestaBaseDatos, bool>(respuesta,bRet);
        }

        /// ************************************************************************************************
        /// <summary>
        /// Borrar los registros excedentes de la tabla Vars
        /// </summary>
        /// ************************************************************************************************
        public void BorrarRegistros()
        {
            string sTabla, sClave;
            bool bRet;

            sTabla = "Vars" + _con.Via.ToString().PadLeft(3, '0');
            sClave = sTabla.Substring(0, 3).ToLower() + "_ID";

            bRet = VerificaRegistro(sTabla,sClave);

            if (bRet)
                _logger.Debug("Se borraron los registros excedentes de {0}",sTabla);

            //TODOg probar delete de registros turnos
            /*bRet = GestionTurno.ChequearBorrarRegistros(MAXIMO_REGISTROS);

            if (bRet)
                _logger.Debug("Se borraron los registros excedentes de Turnos");*/
        }

        /// ************************************************************************************************
        /// <summary>
        /// Ejecuta las consultas para el borrado de registros excedentes de Vars
        /// </summary>
        /// <param name="sTabla">Tabla a revisar</param>
        /// <param name="sDim">columna clave</param>
        /// <returns>True si todo se ejecutó OK, de lo contrario False</returns>
        /// ************************************************************************************************
        private bool VerificaRegistro(string sTabla, string sDim)
        {
            _logger.Trace("Entro");
            bool bRet = false;
            string sConsulta;

            //Cuenta los registros de la tabla sTabla
            sConsulta = $"SELECT COUNT (*) FROM {sTabla}";
            try
            {
                using (SqlConnection _connection = new SqlConnection(_con.LocalConnection))
                using (SqlCommand command = new SqlCommand(sConsulta, _connection))
                {
                    command.CommandTimeout = 2;
                    //Establezco la conexión...
                    _connection.Open();
                    int nRes = 0;

                    //Cuantos registros hay?
                    try
                    {
                        object oResult = command.ExecuteScalar();
                        if (oResult != null)
                        {
                            bRet = Int32.TryParse(oResult.ToString(), out nRes);
                        }
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion Contar. {0}:{1}", e.Number, e.Message);
                        return bRet;
                    }

                    //Tengo la cantidad de registros
                    if (bRet)
                    {
                        if (nRes > MAXIMO_REGISTROS)
                        {
                            sConsulta = $"SELECT TOP 1* FROM {sTabla} ORDER BY {sDim} DESC";
                            command.CommandText = sConsulta;
                            try
                            {
                                object oResult = command.ExecuteScalar();
                                if (oResult != null)
                                {
                                    bRet = Int32.TryParse(oResult.ToString(), out nRes);
                                }
                            }
                            catch (SqlException e)
                            {
                                _logger.Error("Excepcion al tomar el ultimo. {0}:{1}", e.Number, e.Message);
                            }
                        }
                    }

                    //Tengo el ID del ultimo registro, borro los que son menores a este
                    if (bRet)
                    {
                        sConsulta = $"DELETE FROM {sTabla} WHERE {sDim} < {nRes}";
                        command.CommandText = sConsulta;
                        try
                        {
                            command.ExecuteNonQuery();
                            bRet = true;
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion borrar. {0}:{1}", e.Number, e.Message);
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                _logger.Error("Exception {0}:{1}", e.ErrorCode, e.Message);
            }
            catch (Exception e)
            {
                _logger.Error("General Exception {0}.", e.ToString());
            }
            
            _logger.Trace("Salgo");
            return bRet;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Permite reintentar las listas pendientes
        /// </summary>
        /// <returns></returns>
        /// ************************************************************************************************
        public bool Reintenta()
        {
            return _actualiza.ReintentaLista();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Permite conocer el numero de listas pendientes por actualizar
        /// </summary>
        /// <returns>Numero de pendientes</returns>
        /// ************************************************************************************************
        public int NumeroListasPendientes()
        {
            return _actualiza.NumeroListasPendientes();
        }

        /// ************************************************************************************************
        /// <summary>
        /// Inicia la consulta de los SP por tipo de listas y almacena la info en la BD local,
        /// obtiene la version SQL de la estacion, y el sentido de la via
        /// </summary>
        /// <returns>True si pudo obtener la lista de SP, de lo contrario False</returns>
        /// ************************************************************************************************
        public bool ObtenerStoredProcedures()
        {
            bool bRet = false;
            List<Indices> lista = new List<Indices>();

            //Ejecuta Stored Procedure que recopila la información necesaria acerca de los SP a actualizar del cliente
            var task = Task.Run(async () => await _actualiza.ActualizarDB(NOMBRE_TABLA_SP, NOMBRE_SP_LISTAS, lista, false));
            bRet = task.Result;

            //Obtiene la version SQL de la estacion
            if (Consultas("VERSION"))
            {
                string sFecha;
                int nAux;

                //Cuando se consulta la version SQL siempre devuelve un string: Microsoft SQL Server 2017 ~
                int nPos = _versionSql.IndexOf("Server ");
                sFecha = _versionSql.Substring(nPos + 7, 4);

                //Obtiene la fecha de la version
                Int32.TryParse(sFecha, out nAux);

                _actualiza.VersionSQLest = nAux;
            }

            return bRet;
        }

        public bool InicioTablaBaseDatos()
        {
            return Consultas("TABLAS");
        }
    }
}