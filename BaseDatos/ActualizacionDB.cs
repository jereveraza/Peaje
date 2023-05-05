using System;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using ModuloBaseDatos.Entidades;
using System.Threading.Tasks;
using Entidades;
using Utiles;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Clase que contiene los métodos que gestionan las actualizaciones de las tablas de la Base Local.
    /// </summary>
    /// ****************************************************************************************************
    public class ActualizacionDB
    {
        #region Propiedades
        public string Sentido { get; set; }
        public int VersionSQLest { get; set; }
        #endregion

        #region Variables de la clase
        private string _connectionString, _fromString, _fromString2;
        private static int _vSql;
        private static ConfiguracionBaseDatos _con;
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private static List<StoredProcedures> _listaPendientes = new List<StoredProcedures>();
        private static bool _tengoSentido = false;
        #endregion

        #region Constantes
        //Información conocida acerca de la tabla que contiene todos los SP por tipo de lista
        private const string NOMBRE_TABLA_SP = "Lista_Sp", COLUMNA_CODIGO_SP = "til_codig", COLUMNA_NOMBRE_SP = "til_nmbrsp";
        #endregion

        #region Construccion/Destruccion
        /// <summary>
        /// Establece la string de conexión a la Base de Datos.
        /// Arma la sentencia para ejecutar el Stored Procedure, la cual requiere info de la App.config
        /// </summary>
        public void Init()
        {
            _logger.Trace("Entro...");
            //Obtiene configuracion
            _con = Configuraciones.Instance.Configuracion;
            _connectionString = _con.LocalConnection;

            //Variaciones del query segun SP a ejecutar (caso version SQL <= 2008)
            _fromString = $"FROM OPENQUERY([{_con.ServidorPath}], 'SET FMTONLY OFF;SET NOCOUNT ON; EXEC [{_con.ServidorBd}].";
            //Version SQL > 2008
            _fromString2 = $"EXEC [{_con.ServidorPath}].[{_con.ServidorBd}].";

            //Determinar si la version de la BD local es SQL Express 2014 para utilizar otro qry
            _vSql = _con.MotorBd == "SQLEXPRESS2014" ? 2014 : 0;

            _logger.Trace("Salgo...");
        }
        #endregion

        /// ************************************************************************************************
        /// <summary>
        /// Realiza la conexión a la Base de Datos Local.
        /// Ejecuta los diferentes comandos, los cuales se encargarán de actualizar la tabla indicada.
        /// </summary>
        /// <param name="sTabla">El nombre de la tabla en la Base Local</param>
        /// <param name="sSP">El nombre del Stored Procedure</param>
        /// <param name="listaIndices">La lista de índices a agregar en la tabla</param>
        /// <param name="bVersion">True si la version SQL de la estacion es > a 2008</param>
        /// <returns>Retorna True si realizó correctamente la actualización, de lo contario false.</returns>
        /// ************************************************************************************************
        public async Task<bool> ActualizarDB(string sTabla, string sSP, List<Indices> listaIndices, bool bVersion, bool bTbTemporal = false)
        {
            string sConsultaSQL, sIndexSQL, sDropSQL, sDropSQL2, sRenameSQL, sRenameSQL2;
            bool bOkay = false, bAux = true;
            int minTimeout = 10, maxTimeout = 300;

            /* Nota:
             * De acuerdo a la versión SQL de la estación, ciertos cambios se aplicarán...
             * Cambios según bVersion:
             * - Si es True: significa que la versión SQL de la estación es mayor a 2008 por lo tanto hay que
             *   obtener la estructura de los datos resultado de ejecutar el SP. Se debe crear una tabla en la
             *   base de datos local, a partir de esta información, para luego llenarla ejecutando el SP.
             * - Si es False: significa que la versión es menor o igual a 2008 por lo tanto se podrá crear la 
             *   tabla a partir de la ejecución del SP utilizando SET FMTONLY OFF y NOCOUNT ON (esta solución 
             *   no funciona para versiones mayores a SQL Server 2008)
            */
            if (bVersion)
                sConsultaSQL = "INSERT INTO " + sTabla + "_aux " + _fromString2 + sSP;
            else
                sConsultaSQL = "SELECT * INTO " + sTabla + "_aux " + _fromString + sSP + "')";

            //Qry de Drop table
            //Evalua la version SQL de la BD local porque El DELETE IF EXISTS está desde SQL 2016 si la version
            //es menor se utiliza otro.
            if (_vSql == 2014)
            {
                sDropSQL = "IF OBJECT_ID('" + sTabla + "_aux', 'U') IS NOT NULL DROP TABLE " + sTabla + "_aux";
                sDropSQL2 = "IF OBJECT_ID('" + sTabla + "_aux2', 'U') IS NOT NULL DROP TABLE " + sTabla + "_aux2";
            }
            else
            {
                sDropSQL = "DROP TABLE IF EXISTS " + sTabla + "_aux";
                sDropSQL2 = "DROP TABLE IF EXISTS " + sTabla + "_aux2";
            }

            //Qry de Renombrar tablas
            sRenameSQL = "IF OBJECT_ID('" + sTabla + "', 'U') IS NOT NULL EXEC sp_rename 'dbo." + sTabla + "', '" + sTabla + "_aux2'";
            sRenameSQL2 = "EXEC sp_rename 'dbo." + sTabla + "_aux', '" + sTabla + "'";

            _logger.Debug("Entro a actualizar: {0}", sTabla);

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand())
            {
                try
                {
                    command.Connection = connection;
                    //minimo de Timeout para cada comando
                    command.CommandTimeout = minTimeout;
                    //Establezco la conexión
                    await connection.OpenAsync();

                    //Pasos para la actualizacion:
                    /* 1- BORRAR TABLAS AUXILIARES
                        * 1.1: Primero borro la tabla auxiliar para asegurar que no exista, esto en caso de que se haya 
                        * comenzado la actualizacion de una tabla, se creen las tablas auxiliares y luego se
                        * detenga el servicio o no se pueda completar los siguientes pasos, si no se borra la tabla
                        * previamente surgirán conflictos al momento de intentar llenarla o crearla nuevamente. */
                    try
                    {
                        command.CommandText = sDropSQL;
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion borrando {0}_aux [{1}:{2}]", sTabla, e.Number, e.Message);
                        return bOkay;
                    }

                    /* 1.2: Borro la tabla auxiliar 2 para asegurar que no exista (por las mismas razones del paso 1.1) */
                    try
                    {
                        command.CommandText = sDropSQL2;
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion borrando {0}_aux2 [{1}:{2}]", sTabla, e.Number, e.Message);
                        return bOkay;
                    }

                    /* 2- COMPROBAR VERSION SQL DE LA ESTACION Y GENERAR TABLA AUXILIAR
                        * Este paso depende de la version SQL con la que se está trabajando, si es mayor a 2008 se
                        * ejecutará el paso 2.1, de lo contrario el paso 2.2. En el caso de utilizar el paso 2.1
                        * se tendrá que ejecutar un paso adicional (2.3) para llenar la tabla con los datos.
                        * 2.1: Crea la tabla a partir de la estructura proporcionada por: sys.dm_exec_describe_first_result_set */
                    if (bVersion)
                    {
                        List<ResultSet> listaCampos = new List<ResultSet>();
                        string sQuery = string.Empty, sName = string.Empty;
                        int nPos = sSP.IndexOf(_con.Estacion + ",");

                        if (nPos > 0)
                            sName = sSP.Substring(0, nPos - 1);
                        else
                            sName = sSP;

                        //Si el Stored Procedure utiliza una tabla temporal, no se podrá obtener la estructura
                        //En estos casos será necesario el uso del SP extra que tiene el mismo nombre del SP pero
                        //con "SD" agregado al final
                        if (bTbTemporal)
                            sName += "SD";

                        //Query a la estructura (sys.dm_exec_describe_first_result_set) y almacena en listaCampos
                        try
                        {
                            sQuery = $"SELECT * FROM OPENQUERY([{_con.ServidorPath}],'SELECT * FROM " +
                                        $"sys.dm_exec_describe_first_result_set (''{_con.ServidorBd}.{sName}'', NULL, 0)')";
                            command.CommandText = sQuery;
                            using (SqlDataReader reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    string sEsNull = string.Empty;
                                    ResultSet rs = new ResultSet();
                                    rs.NombreCampo = reader["name"].ToString();
                                    rs.TipoDato = reader["system_type_name"].ToString();
                                    rs.EsNulo = reader["is_nullable"].ToString() == "True" ? true : false;
                                    listaCampos.Add(rs);
                                }
                            }
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion obteniendo estructura de los resultados de {0} [{1}:{2}]", sTabla, e.Number, e.Message);
                            return bOkay;
                        }

                        //Crear la tabla a partir de los campos encontrados utilizando listaCampos...
                        try
                        {
                            sQuery = $"CREATE TABLE {sTabla}_aux (";

                            foreach (ResultSet campo in listaCampos)
                            {
                                string sNull = string.Empty;

                                if (campo.EsNulo)
                                    sNull = "NULL";
                                else
                                    sNull = "NOT NULL";
                                //Ejemplo:     cam_id                  varchar(10)         not null
                                sQuery += campo.NombreCampo + " " + campo.TipoDato + " " + sNull + ",";
                            }

                            //Se termina de armar la consulta para crear la tabla
                            sQuery = sQuery.Substring(0, sQuery.Length - 1);
                            sQuery += ");";

                            command.CommandText = sQuery;
                            await command.ExecuteNonQueryAsync();
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion creando la tabla {0}_aux [{1}:{2}] Query[{3}]", sTabla, e.Number, e.Message, sQuery);
                            return bOkay;
                        }
                    }
                    /* 2.2: Crea la tabla a partir de la propia ejecución del SP */
                    else
                    {
                        //Ejecuto el Stored Procedure desde el Linked Server y lo guardo en una Tabla auxiliar
                        try
                        {
                            command.CommandText = sConsultaSQL;
                            //Aumento Timeout porque generalmente actualizaciones como la tabla de Tags se tarda mucho.
                            command.CommandTimeout = maxTimeout;
                            await command.ExecuteNonQueryAsync();
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("SQL Excepcion ejecutando SP: {0} [{1}:{2}]", sSP, e.Number, e.Message);
                            return bOkay;
                        }
                    }

                    /* 2.3: Si la versión es mayor a 2008, llena la tabla que se acaba de crear a partir de la estructura 
                        * resultado del SP realizando un INSERT INTO */
                    if (bVersion)
                    {
                        try
                        {
                            command.CommandText = sConsultaSQL;
                            //Aumento Timeout porque generalmente actualizaciones como la tabla de Tags se tarda mucho.
                            command.CommandTimeout = maxTimeout;
                            await command.ExecuteNonQueryAsync();
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("SQL Excepcion poblando la tabla con SP: {0} [{1}:{2}]", sSP, e.Number, e.Message);
                            return bOkay;
                        }
                    }

                    //devuelvo el minimo de timeout
                    command.CommandTimeout = minTimeout;

                    /* 3- CREAR LOS INDICES DE BUSQUEDA DE LA TABLA
                        * Se crean los indices a partir de la informacion proporcionada por listaIndices previamente obtenida
                        * al solicitar la tabla que contiene información acerca de los SP a ejecutar por tipo de lista */
                    if ((listaIndices.Count() > 0))
                    {
                        try
                        {
                            foreach (Indices Indice in listaIndices)
                            {
                                if (!string.IsNullOrEmpty(Indice.Clustered))
                                    sIndexSQL = $"CREATE CLUSTERED INDEX Cluster ON {sTabla}_aux ({Indice.Clustered})";
                                else if (!string.IsNullOrEmpty(Indice.NombreIndice))
                                    sIndexSQL = "CREATE INDEX " + Indice.NombreIndice + " ON " + sTabla + "_aux (" + Indice.CampoIndice + ")";
                                else if (!string.IsNullOrEmpty(Indice.PrimaryKey))
                                    sIndexSQL = "ALTER TABLE " + sTabla + "_aux ADD PRIMARY KEY (" + Indice.PrimaryKey + ")";
                                else
                                    sIndexSQL = "";

                                command.CommandText = sIndexSQL;
                                await command.ExecuteNonQueryAsync();
                            }
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion creando los indices [{0}:{1}]", e.Number, e.Message);
                            bAux = false;
                            //return bOkay; No devuelvo para que cree la tabla sin indices
                        }
                    }

                    /* 4- RENOMBRAR LA TABLA sTabla A sTabla_aux2
                        * Se renombra la tabla que actualmente se está utilizando para buscar información */
                    try
                    {
                        command.CommandText = sRenameSQL;
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion renombrando la tabla auxiliar 2 [{0}:{1}]", e.Number, e.Message);
                        return bOkay;
                    }

                    /* 5- RENOMBRAR LA TABLA sTabla_aux A sTabla
                        * Se renombra la tabla que se creó y llenó con la información actualizada para ser utilizada*/
                    try
                    {
                        command.CommandText = sRenameSQL2;
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion renombrando la tabla {0} [{1}:{2}]", sTabla, e.Number, e.Message);
                        return bOkay;
                    }

                    /* 6- ELIMINAR LA TABLA sTabla_aux2
                        * Ya no nos interesa esta tabla porque es vieja */
                    try
                    {
                        command.CommandText = sDropSQL2;
                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion borrando {0}_aux2 [{1}:{2}]", sTabla, e.Number, e.Message);
                        return bOkay;
                    }

                    /*Verificar cuantos registros tiene (en caso de que al ejecutar SP no traiga nada), solo si es la tabla de listas de SP*/
                    if (sTabla == NOMBRE_TABLA_SP)
                    {
                        try
                        {
                            command.CommandText = $"SELECT COUNT (*) FROM {sTabla}";
                            using (SqlDataReader reader = await command.ExecuteReaderAsync())
                            {
                                string sCantidad = "";
                                while (await reader.ReadAsync())
                                {
                                    sCantidad += reader[0].ToString();
                                }
                                _logger.Debug("Cantidad de registros en ListaSP = {0}", sCantidad);
                            }
                        }
                        catch (SqlException e)
                        {
                            _logger.Error("Excepcion consultando registros en tabla {0} [{1}:{2}]", sTabla, e.Number, e.Message);
                            return bOkay;
                        }
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error("SQL Excepcion [{0}:{1}]", e.Number, e.Message);
                    return bOkay;
                }
                catch (Exception e)
                {
                    _logger.Error("Otras Excepciones [{0}, {1}]", e.Message, sTabla);
                    return bOkay;
                }

                bOkay = bAux ? true : false; //si falló agregar indices, se toma en consideracion para reintento
            }
            return bOkay;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Ejecuta todos los SP correspondientes al tipo de lista proporcionado
        /// </summary>
        /// <param name="sTipoLista">Tipo de lista a actualizar</param>
        /// <returns>Tuple que contiene si se pudo actualizar o no la lista, y el numero de SP faltantes
        /// si los hubo</returns>
        /// ************************************************************************************************
        public async Task<Tuple<bool,int>> ActualizaPorTipoLista(string sTipoLista = "")
        {
            _logger.Trace("Entro...");
            List<Indices> lIndices = new List<Indices>();
            List<StoredProcedures> listaSPTipo = null;
            List<StoredProcedures> listaAuxiliar = new List<StoredProcedures>();
            int nTotal = 0, nFalla = 0;
            bool bRet = false;
            string sParam = string.Empty, sSPfinal = string.Empty;

            //Obtiene todos los SP a ejecutar por el tipo de lista, consultando a la BD local
            listaSPTipo = ConsultaLocal(sTipoLista,COLUMNA_CODIGO_SP);

            //Si desde supervisión se habia enviado el comando DESCONECTAR, me salgo porque no se supone que deba
            //permitir la actualizacion de listas...
            if(AsynchronousSocketListener.GetSupervConn() == "N")
            {
                _logger.Info("No se pudo actualizar el Tipo de Lista : {0} " +
                             "debido a que se envió el comando de Desconexión desde Supervisión",sTipoLista);
                return new Tuple<bool, int>(bRet, nFalla);
            }

            try
            {
                //Evalua si hay listas por ese tipo...
                if (listaSPTipo.Count() > 0)
                {
                    nTotal = listaSPTipo.Count();

                    //Evalua cada uno de los SP y extrae información necesaria
                    foreach (StoredProcedures sp in listaSPTipo)
                    {
                        bool bTemp = false;
                        try
                        {
                            ArmarIndices(sp, ref lIndices);
                            
                            //la versión SQL de la estación es mayor a 2008?
                            bool bVar = VersionSQLest > 2008 ? true : false;

                            //Obtengo los parametros necesarios para ejecutar el SP. Ejemplo: ViaNet.usp_GetTags 1,51 ....
                            sParam = ObtenerParametros(sp.ParametrosSP);
                            //Se arma el string con el SP y lso parametros
                            sSPfinal = "ViaNet." + sp.NombreSP + " " + sParam;

                            //Si el SP tiene tabla temporal lo indica
                            if (sp.TieneTablaTemporal == "S")
                                bTemp = true;

                            //Se actualiza el SP seleccionado
                            bool bOk = await ActualizarDB(sp.NombreTabla, sSPfinal, lIndices, bVar, bTemp);

                            if (bOk)
                                _logger.Info("Tabla: {0}. Finalizó correctamente", sp.NombreTabla);
                            else
                            {
                                nFalla++;
                                //Guarda el SP en una lista auxiliar antes de guardar en la lista de pendientes
                                listaAuxiliar.Add(sp);

                                _logger.Info("Tabla: {0}. No Finalizó correctamente, se agrega a lista de pendientes", sp.NombreTabla);
                            }

                            //Borra los indices para llenar la lista nuevamente
                            if (lIndices.Any())
                                lIndices.Clear();
                        }
                        catch (Exception e)
                        {
                            _logger.Error("Excepción general actualizando tabla {0}: {1}", sp.NombreTabla, e.Message);
                        }

                        if (!_tengoSentido)
                        {
                            _tengoSentido = ObtenerSentido();
                        }

                    }

                    //Evalua si el total de listas es diferente a las listas fallidas, esto debido a que si fallan todas es mejor
                    //reintentar la actualizacion mediante el timer de BuscarActualizaciones, no el de reintentar listas
                    if (nTotal != nFalla)
                    {
                        foreach (StoredProcedures faltantes in listaAuxiliar)
                        {
                            //Añade el SP fallido a la lista de pendientes
                            if (!_listaPendientes.Exists(x => x.NombreTabla == faltantes.NombreTabla))
                                _listaPendientes.Add(faltantes);
                        }

                        //Retorna true porque pudo actualizar al menos 1 de las listas
                        bRet = true;
                    }
                }
                else
                    _logger.Debug($"No se encontraron listas a actualizar por el tipo {sTipoLista}");
            }
            catch (Exception e)
            {
                _logger.Error("out Excepcion {0}",e.Message);
            }

            _logger.Trace("Salgo...");

            return new Tuple<bool,int>(bRet,nFalla);
        }

        /// ************************************************************************************************
        /// <summary>
        /// Actualiza los SP almacenados en la lista de pendientes
        /// </summary>
        /// <param name="sNombreTabla">El nombre del SP que no se pudo ejecutar</param>
        /// <returns>True si pudo actualizar, de lo contrario False</returns>
        /// ************************************************************************************************
        private bool ActualizaPorPendiente(string sNombreTabla)
        {
            _logger.Trace("Entro...");
            bool bRet = false;
            List<StoredProcedures> listaReintenta = new List<StoredProcedures>();
            List<Indices> lIndices = new List<Indices>();
            string sParam = string.Empty, sSPfinal = string.Empty;

            //Busca en la lista de SP de la Bd local la información correspondiente al SP pendiente
            //Realiza la busqueda por nombre
            listaReintenta = ConsultaLocal(sNombreTabla, COLUMNA_NOMBRE_SP);

            foreach (StoredProcedures sp in listaReintenta)
            {
                bool bTemp = false;
                ArmarIndices(sp, ref lIndices);

                //la versión SQL de la estación es mayor a 2008?
                bool bVar = VersionSQLest > 2008 ? true : false;

                //Obtengo los parametros necesarios para ejecutar el SP. Ejemplo: ViaNet.usp_GetTags 1,51 ....
                sParam = ObtenerParametros(sp.ParametrosSP);
                sSPfinal = "ViaNet." + sp.NombreSP + " " + sParam;

                if (sp.TieneTablaTemporal == "S")
                    bTemp = true;

                //Envio el SP para que se actualice
                var task = Task.Run(async () => await ActualizarDB(sp.NombreTabla, sSPfinal, lIndices, bVar, bTemp));
                bRet = task.Result;

                if (bRet)
                    _logger.Info("Tabla: {0}. Finalizó correctamente", sp.NombreTabla);
            }

            _logger.Trace("Salgo...");
            return bRet;
        }

        /// <summary>
        /// Evalua cada uno de los SP y extrae información necesaria para armar los indices
        /// </summary>
        /// <param name="listaSPTipo">Lista de SP</param>
        /// <returns>Lista de Indices</returns>
        private void ArmarIndices(StoredProcedures sp, ref List<Indices> lIndices)
        {
            Indices li;
            string sRestante = "";
            //Extrae los indices y guarda en una lista
            try
            {
                //El indice cluster estará despues del símbolo "/", lo extraemos, si lo hay
                if (sp.IndicesTabla.Contains("/"))
                {
                    string[] indice = sp.IndicesTabla.Split('/');
                    li = new Indices();
                    li.Clustered = indice[1];
                    lIndices.Add(li);
                    sRestante = indice[0];
                    sp.IndicesTabla = sp.IndicesTabla.Remove(sp.IndicesTabla.IndexOf('/'));
                }

                //los indices non-clustered se separan por el simbolo "|", los extraemos
                if (sp.IndicesTabla.Contains("|"))
                {
                    string[] indice;
                    if (!string.IsNullOrEmpty(sRestante))
                        indice = sRestante.Split('|');
                    else
                        indice = sp.IndicesTabla.Split('|');

                    for (int i = 0; i < indice.Count(); i++)
                    {
                        li = new Indices();
                        li.NombreIndice = "Indice" + i;
                        li.CampoIndice = indice[i];
                        lIndices.Add(li);
                    }
                }
                //si no hay simbolo "|" es porque solo es un indice non-clustered
                else if (!string.IsNullOrEmpty(sp.IndicesTabla))
                {
                    li = new Indices();
                    li.NombreIndice = "Indice1";
                    if (string.IsNullOrEmpty(sRestante))
                        li.CampoIndice = sp.IndicesTabla;
                    else
                        li.CampoIndice = sRestante;
                    lIndices.Add(li);
                }
                //extraemos el campo que será primary key (automáticamente se convertirá en cluster)
                if (!string.IsNullOrEmpty(sp.PrimaryKey))
                {
                    li = new Indices();
                    li.PrimaryKey = sp.PrimaryKey;
                    lIndices.Add(li);
                }
            }
            catch(Exception e)
            {
                _logger.Error("Excepción producida: {0}",e.Message);
            }
        }

        /// ************************************************************************************************
        /// <summary>
        /// Evalua los parametros que requiere el SP y determina los valores correspondientes
        /// </summary>
        /// <param name="sParametros">Los parametros que requiere el SP para ser ejecutado</param>
        /// <returns>String con los parametros agregados</returns>
        /// ************************************************************************************************
        private string ObtenerParametros(string sParametros)
        {
            string[] parametros;
            string sFinal = string.Empty;

            if (sParametros.Contains(","))
            {
                parametros = sParametros.Split(',');

                //Todo busca otra manera de evaluar esto...
                if (parametros.Count() > 3 && parametros[3] == "@Ruc")
                    parametros[2] = "@aux";

                //Minimo siempre tienen 2 parametros que son: estacion y via.... Recorro los parametros...
                foreach (string par in parametros)
                {
                    string aux = par.Replace("@", "");

                    switch (aux)
                    {
                        //Numero de estacion
                        case "nuestac":
                            {
                                sFinal += _con.Estacion + ",";
                                break;
                            }
                        //Numero de via
                        case "nuvia":
                            {
                                sFinal += _con.Via + ",";
                                break;
                            }
                        //sentido de la via
                        case "senti":
                            {
                                sFinal += Sentido + ",";
                                break;
                            }
                        //parametros relacionados con lista de tags, negra y exentos... no es necesario especificarlos
                        case "lista":
                        case "paten":
                        case "tag":
                        case "tipotarj":
                            {
                                sFinal += "null,";
                                break;
                            }
                        //lista completa? 'S', RevisarPrepago? tags que tienen cuenta prepago y pago en via, enviar 'S'
                        case "licom":
                        case "RevisarPrepago":
                            {
                                sFinal += "'S',";
                                break;
                            }
                        //Tipo de lista? 'N' negra (lista negra de Tags)
                        case "tipli":
                            {
                                sFinal += "'N',";
                                break;
                            }
                        //Tipo: Busqueda 'O', Lista 'L'. Se deja lista porque la busqueda se realiza hacia la BD local...
                        case "tipo":
                            {
                                sFinal += "'L',";
                                break;
                            }
                        default:
                            {
                                sFinal += "";
                                break;
                            }
                    }
                }
                //Termina de armar la lista de parametros: 1,51,null,'S', como termina con una ',' se la elimina ...
                sFinal = sFinal.Substring(0, sFinal.Length - 1);
            }
            
            return sFinal;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Recorre la lista de SP que no se pudieron actualizar para reintentarlos
        /// </summary>
        /// <returns>True si pudo actualizar las listas pendientes, de lo contrario False</returns>
        /// ************************************************************************************************
        public bool ReintentaLista()
        {
            _logger.Trace("Entro...");
            bool bEmpty = true;
            List<StoredProcedures> listaAuxiliar = new List<StoredProcedures>();
            listaAuxiliar = _listaPendientes;

            //recorre lista de pendientes, y va sacando elementos a medida de que los puede actualizar...
            foreach (StoredProcedures sp in listaAuxiliar.ToArray())
            {
                try
                {
                    _logger.Debug("Reintentamos lista {0}", sp.NombreTabla);
                    //remueve elemento si se pudo actualizar
                    if (ActualizaPorPendiente(sp.NombreSP))
                        _listaPendientes.Remove(sp);
                }
                catch (Exception e)
                {
                    _logger.Error("Excepción al reintentar lista {0} pendientes... {1}",sp.NombreTabla, e.Message);
                }
            }

            //si queda algun elemento en la lista devuelve False porque no lo pudo completar
            if (_listaPendientes.Any())
                bEmpty = false;

            _logger.Trace("Salgo...");
            return bEmpty;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Proporciona información acerca del numero de SP pendientes
        /// </summary>
        /// <returns>Numero de SP pendientes</returns>
        /// ************************************************************************************************
        public int NumeroListasPendientes()
        {
            int n;

            try
            {
                n = _listaPendientes.Count();
            }
            catch (Exception)
            {
                n = 0;
            }

            return n;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Consulta en la base de datos local según información deseada
        /// </summary>
        /// <returns>Retorna en una lista los SP encontrados</returns>
        /// ************************************************************************************************
        private List<StoredProcedures> ConsultaLocal(string sFiltro = "", string sColumn = "")
        {
            _logger.Trace("Entro...");
            List<StoredProcedures> listaSP = new List<StoredProcedures>();
            string sConsultaSQL = string.Empty;

            using (SqlConnection connection = new SqlConnection(_connectionString))
            using (SqlCommand command = new SqlCommand())
            {
                try
                {
                    command.Connection = connection;
                    command.CommandTimeout = 2;
                    connection.Open();

                    //si no se especificó un filtro busco todos los SP
                    if (string.IsNullOrEmpty(sFiltro))
                        sConsultaSQL = $"SELECT * FROM {NOMBRE_TABLA_SP}";
                    else
                    {
                        if (sColumn == COLUMNA_NOMBRE_SP)
                            sConsultaSQL = $"SELECT * FROM {NOMBRE_TABLA_SP} WHERE {COLUMNA_NOMBRE_SP} = '{sFiltro}'";
                        else if (sColumn == COLUMNA_CODIGO_SP)
                            sConsultaSQL = $"SELECT * FROM {NOMBRE_TABLA_SP} WHERE {COLUMNA_CODIGO_SP} = '{sFiltro}'";
                    }

                    command.CommandText = sConsultaSQL;

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader != null)
                        {
                            //Almaceno en lista a retornar segun los registros que encuentre
                            while (reader.Read())
                            {
                                StoredProcedures sp = new StoredProcedures();
                                sp.CodigoLista = reader[0].ToString();
                                sp.NombreSP = reader[1].ToString();
                                sp.NombreTabla = reader[2].ToString();
                                sp.IndicesTabla = reader[3].ToString();
                                sp.PrimaryKey = reader[4].ToString();
                                sp.ParametrosSP = reader[5].ToString();
                                sp.TieneTablaTemporal = reader[6].ToString();
                                listaSP.Add(sp);
                            }
                        }
                        else
                        {
                            //Algo salio mal con el reader, indico error
                            _logger.Error("Reader vacío");
                        }
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error("SQL Excepcion. Consulta realizada: {0}:{1}", e.Number, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error("Otras Excepciones: {0}", e.ToString());
                }
            }

            //Reordeno lista de SP por prioridad
            var confi = listaSP.Find(x => x.NombreTabla == ClassUtiles.GetEnumDescr(eTablaBD.ConfiguracionDeVia)); //busca si está config_via en la lista

            if(confi != null && listaSP.Count > 0)
            {
                listaSP.Remove(confi); //remueve de la lista
                listaSP.Insert(0, confi); //inserta al inicio
            }

            var frmtck = listaSP.Find(x => x.NombreTabla == ClassUtiles.GetEnumDescr(eTablaBD.FormatoDeTickets)); //busca si está formato_ticket en la lista

            if (frmtck != null && listaSP.Count > 2)
            {
                listaSP.Remove(frmtck); //remueve de la lista
                listaSP.Insert(1, frmtck); //inserta al inicio
            }

            _logger.Trace("Salgo...");
            return listaSP;
        }

        /// <summary>
        /// Obtiene el sentido de la vía segun ConfigVia
        /// </summary>
        private bool ObtenerSentido()
        {
            _logger.Trace("Entro...");
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
                    //Obtiene el sentido de la via consultando viadef
                    sConsultaSQL = $"SELECT via_sente from {Utility.ObtenerDescripcionEnum(eTablaBD.ConfiguracionDeVia)}";

                    command.CommandText = sConsultaSQL;
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader != null)
                        {
                            if (reader.Read())
                            {
                                Sentido = reader[0].ToString();
                                bRet = true;
                                _logger.Debug("El sentido es: {0}...", Sentido);
                            }
                        }
                        else
                        {
                            //Algo salio mal con el reader, indico error
                            _logger.Error("Error en la lectura de la BD");
                        }
                    }
                }
                catch (SqlException e)
                {
                    _logger.Error("SQL Excepcion. Consulta realizada: {0}:{1}", e.Number, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error("Otras Excepciones. {0}", e.ToString());
                }
            }
            _logger.Trace("Salgo...");
            return bRet;
        }
    }
}