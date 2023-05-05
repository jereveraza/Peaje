using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionBaseDatos;
using ModuloBaseDatos.Entidades;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;

namespace ModuloBaseDatos
{
    public class GestionTurno
    {
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private static int _turnoActual = 0, _turnoAnterior = 0, _numeroTurno = 0, _turnoTot = 0;
        private static bool _hayTurnoAbierto = false, _hayNumeradoresTurno = false, _numeracionVieja = false, _numeradorCorrupto = false, _localActualizada = false;
        private static string _connectionString = "";
        private static DateTime FECHA_MIN_SQL = new DateTime(1900,01,01);
        public string Connection { get { return _connectionString; } set { _connectionString = value; } }

        /// ************************************************************************************************
        /// <summary>
        /// Evalúa qué acción ejecutar según lo deseado por la aplicación de vía
        /// </summary>
        /// <param name="dbParam">Acción que solicita la vía</param>
        /// <returns>Respuesta a la vía</returns>
        /// ************************************************************************************************
        public RespuestaBaseDatos AccionVia(SolicitudBaseDatos dbParam)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = null;
            //Stopwatch sw = new Stopwatch();
            //sw.Restart();

            //Evalua la tabla que quiere actualizar 
            switch (dbParam.Tabla)
            {
                case eTablaBD.SetApertura:
                case eTablaBD.GetUltimoTurno:
                case eTablaBD.SetParte:
                case eTablaBD.SetCodImp:
                case eTablaBD.SetCierre:
                    {
                        respuesta = GuardarTurno(dbParam);

                        break;
                    }
                case eTablaBD.InitNum:
                case eTablaBD.SetNumerador:
                case eTablaBD.UltTurnoNum:
                case eTablaBD.SetFechaNumerador:
                case eTablaBD.SetEstadoNumerador:
                    {
                        respuesta = GuardarNumeradores(dbParam);

                        break;
                    }
                case eTablaBD.GetInfo:
                case eTablaBD.GetNumeroTurno:
                case eTablaBD.GetNumerador:
                case eTablaBD.GetFechaNumerador:
                case eTablaBD.GetEstadoNumerador:
                    {
                        respuesta = BuscarInfo(dbParam);

                        break;
                    }
                    
                case eTablaBD.SetTransito:
                    {
                        if (_turnoActual == 0 && _turnoAnterior == 0)
                            break;

                        respuesta = GuardarTransito(dbParam);


                        break;
                    }
                case eTablaBD.SetAnomalia:
                    {
                        if (_turnoActual == 0 && _turnoAnterior == 0)
                            break;

                        respuesta = GuardarAnomalia(dbParam);

                        break;
                    }
                case eTablaBD.SetVenta:
                    {
                        if (_turnoActual == 0 && _turnoAnterior == 0)
                            break;

                        respuesta = GuardarVenta(dbParam);

                        break;
                    }
                case eTablaBD.SetOtro:
                    {
                        if (_turnoActual == 0 && _turnoAnterior == 0)
                            break;

                        respuesta = GuardarOtroMonto(dbParam);

                        break;
                    }
                case eTablaBD.GetTransitosTotalesOtraVia:
                case eTablaBD.GetTransitosTotales:
                    {
                        string totales = "";
                        bool bVia = dbParam.Tabla == eTablaBD.GetTransitosTotalesOtraVia ? true : false;

                        totales = GetTransitosTotales(bVia, dbParam.Filtro);

                        respuesta = new RespuestaBaseDatos();

                        if (string.IsNullOrEmpty(totales))
                        {
                            respuesta.CodError = EnmErrorBaseDatos.Falla;
                        }
                        else
                        {
                            respuesta.CodError = EnmErrorBaseDatos.SinFalla;
                            respuesta.RespuestaDB = totales;
                        }

                        respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);

                        break;
                    }
                case eTablaBD.SetInfoVia:
                    {
                        respuesta = GuardarInfoVia(dbParam);
                        break;
                    }
            }

            //Chequea ID turno
            try
            {
                var context = new Turno();
                var query = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno).FirstOrDefault();

                if(query != null)
                {
                    if (query.IDTurno > _turnoActual)
                    {
                        _turnoAnterior = _turnoActual;
                        _turnoActual = query.IDTurno;
                    }

                    if (_numeroTurno == 0)
                        _numeroTurno = query.NumTurno;
                }
            }
            catch(Exception e)
            {
                _logger.Error(e);
            }

            _logger.Trace("Salgo...");

            //sw.Stop();

            //_logger.Debug($"Tiempo para Realizar: {dbParam.Tabla.ToString()} {sw.ElapsedMilliseconds}");

            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla Turnos
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarTurno(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;
            int nAux1 = 0;

            try
            {
                var context = new Turno();
                Turnos aperturaTurno = new Turnos();

                switch (solicitud.Tabla)
                {
                    case eTablaBD.SetApertura:
                        if(_hayTurnoAbierto)
                        {
                            _logger.Info("Ya hay un turno abierto, no se almacenará otro sin antes haberlo cerrado...");
                            respuesta.CodError = EnmErrorBaseDatos.Falla;
                            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                            return respuesta;
                        }
                        else
                        {
                            aperturaTurno = JsonConvert.DeserializeObject<Turnos>(solicitud.Filtro);
                            aperturaTurno.NumTurno = ++_numeroTurno;
                            context.Turnos.Add(aperturaTurno);
                            _hayTurnoAbierto = true;
                        }
                       break;
                    case eTablaBD.GetUltimoTurno:
                        solicitud.Filtro = solicitud.Filtro.Replace("[", "");
                        solicitud.Filtro = solicitud.Filtro.Replace("]", "");
                        UltTurno oUlt = JsonConvert.DeserializeObject<UltTurno>(solicitud.Filtro);

                        //revisa si ya hay un turno con el mismo numero de turno, de lo contrario inserta un registro nuevo
                        if (!context.Turnos.Any(c => c.NumTurno == oUlt.NumeroTurno))
                        {
                            aperturaTurno.NumTurno = oUlt.NumeroTurno;
                            aperturaTurno.TurnoAbierto = "N";
                            aperturaTurno.Sentido = oUlt.Sentido == "A" ? "Ascendente" : "Descendente";
                            aperturaTurno.FechaAper = DateTime.Now;
                            aperturaTurno.FechaCierre = DateTime.Now;
                            context.Turnos.Add(aperturaTurno);
                            _numeroTurno = oUlt.NumeroTurno;
                        }
                        else
                        {
                            //se buscar el nro de turno del ultimo registro de Turnos
                            var queryGNT = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno).Select(f => f.NumTurno).FirstOrDefault();

                            //si el nro de turno de la BD local es mayor, está mas actualizada la BD local
                            if (queryGNT > 0 && queryGNT > oUlt.NumeroTurno)
                            {
                                _localActualizada = true;
                                _logger.Info("El nro de Turno local está mas actualizado que el proveniente de la plaza. La BD local está mas actualizada.");
                            }

                            var query = context.Numeradores.Where(p => p.IDTurno == _turnoActual).ToList();
                            var lastOne = Enum.GetValues(typeof(eContadores)).Cast<eContadores>().Max();
                            if (query != null && query.Count >= (int)lastOne)
                                _numeracionVieja = true;
                            else
                                _numeradorCorrupto = true;
                        }
                        break;
                    case eTablaBD.SetParte:
                        var parte = context.Turnos.Find(_turnoActual);
                        if (parte != null)
                        {
                            if (parte.TurnoAbierto == "S")
                            {
                                int nAux;
                                bool bOk;
                                solicitud.Filtro = Utility.DeserializarFiltro(solicitud.Filtro);
                                bOk = int.TryParse(solicitud.Filtro, out nAux);
                                parte.NroParte = bOk ? nAux : 0;
                            }
                        }
                        break;
                    case eTablaBD.SetCodImp:
                        var codimp = context.Turnos.Find(_turnoActual);

                        if (codimp != null)
                        {
                            solicitud.Filtro = Utility.DeserializarFiltro(solicitud.Filtro);
                            codimp.NumImpFiscal = solicitud.Filtro;
                        }
                        break;
                    case eTablaBD.SetCierre:
                        nAux1 = _turnoActual;
                        solicitud.Filtro = Utility.DeserializarFiltro(solicitud.Filtro);
                        DateTime dtFechaCierre = DateTime.Parse(solicitud.Filtro);
                        var cierre = context.Turnos.Find(_turnoActual);

                        // Si es igual no es un cierre, solo obtiene los totales
                        if( dtFechaCierre != DateTime.MinValue )
                        {
                            if( cierre != null )
                            {
                                cierre.FechaCierre = dtFechaCierre;
                                cierre.TurnoAbierto = "N";
                                _turnoActual = 0;
                                _turnoAnterior = nAux1;
                                _hayTurnoAbierto = false;
                                _hayNumeradoresTurno = false;
                                _turnoTot = _turnoAnterior;
                            }
                        }
                        break;
                }
                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.SinFalla && solicitud.Tabla == eTablaBD.SetCierre)
                respuesta.RespuestaDB = GetTotales(nAux1);
            else if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla numeradores
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarNumeradores(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;
            string sRespuesta = string.Format("Se ejecutó acción. Turno: {0}. eTabla: {1}.", _turnoActual, solicitud.Tabla);

            try
            {
                var context = new Turno();
                switch (solicitud.Tabla)
                {
                    case eTablaBD.InitNum:
                        if (_hayNumeradoresTurno)
                        {
                            _logger.Info("Ya hay numeradores para este turno...");
                            respuesta.CodError = EnmErrorBaseDatos.Falla;
                            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                            return respuesta;
                        }
                        else
                        {
                            var last = Enum.GetValues(typeof(eContadores)).Cast<eContadores>().Max();

                            if (_turnoAnterior > 0)
                            {
                                for (int i = 0; i <= (int)last; i++)
                                {
                                    eContadores contador = (eContadores)i;
                                    Numeradores numer = new Numeradores();

                                    var nu = context.Numeradores.Find(_turnoAnterior, contador.ToString());

                                    if (nu != null)
                                    {
                                        numer.IDTurno = _turnoActual;
                                        numer.Tipo = contador.ToString();
                                        numer.ValorIni = nu.ValorFin == 0? nu.ValorFin : nu.ValorFin + 1;
                                        numer.ValorFin = nu.ValorFin;
                                        numer.UltimaFecha = DateTime.Now;
                                        context.Numeradores.Add(numer);
                                    }
                                }

                                //Almacena nuevo registro de la fecha de ultima actualizacion y el estado actual
                                /*var buscar = context.Datos_Numeraciones.Find(_turnoActual);
                                if (buscar != null)
                                {
                                    buscar.Estado = eEstadoNumeracion.NumeracionOk.ToString();
                                }
                                else
                                {
                                    DatosNumeraciones oDatos = new DatosNumeraciones();
                                    oDatos.IDTurno = _turnoActual;
                                    oDatos.UltimaNum = DateTime.Now;
                                    oDatos.Estado = eEstadoNumeracion.NumeracionOk.ToString();
                                    context.Datos_Numeraciones.Add(oDatos);
                                }*/
                            }
                            _hayNumeradoresTurno = true;
                            break;
                        }
                    case eTablaBD.SetNumerador:
                        if (_turnoActual == 0 && _turnoAnterior == 0)
                            break;

                        Numeradores numsTurno = new Numeradores();
                        numsTurno = JsonConvert.DeserializeObject<Numeradores>(solicitud.Filtro);

                        var vnum = context.Numeradores.Find(_turnoActual, numsTurno.Tipo);

                        if (vnum == null)
                        {
                            numsTurno.IDTurno = _turnoActual;
                            if (numsTurno.ValorIni == 0)
                                numsTurno.ValorIni = numsTurno.ValorFin;
                            numsTurno.UltimaFecha = DateTime.Now;
                            context.Numeradores.Add(numsTurno);
                        }
                        else
                        {
                            vnum.ValorFin = numsTurno.ValorFin;
                            vnum.UltimaFecha = DateTime.Now;
                            var fecha = context.Datos_Numeraciones.Find(_turnoActual);
                            if (fecha != null)
                            {
                                fecha.UltimaNum = DateTime.Now;
                            }
                        }

                        sRespuesta += " Valor: " + numsTurno.ValorFin;
                        break;
                    case eTablaBD.UltTurnoNum:
                        UltTurno oUlt = JsonConvert.DeserializeObject<UltTurno>(solicitud.Filtro);
                        Dictionary<eContadores, Numeradores> nums = new Dictionary<eContadores, Numeradores>();

                        #region Seteo Numeradores
                        Numeradores oNum = new Numeradores();
                        oNum.Tipo = eContadores.NumeroTicketFiscal.ToString();
                        oNum.ValorIni = oUlt.UltimoTicketFiscal == 0 ? oUlt.UltimoTicketFiscal : oUlt.UltimoTicketFiscal + 1;
                        oNum.ValorFin = oUlt.UltimoTicketFiscal;
                        oNum.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroTicketFiscal, oNum);

                        Numeradores oNum2 = new Numeradores();
                        oNum2.Tipo = eContadores.TicketAutoPaso.ToString();
                        oNum2.ValorIni = oUlt.UltimoAutoPaso == 0 ? oUlt.UltimoAutoPaso : oUlt.UltimoAutoPaso + 1;
                        oNum2.ValorFin = oUlt.UltimoAutoPaso;
                        oNum2.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.TicketAutoPaso, oNum2);

                        Numeradores oNum3 = new Numeradores();
                        oNum3.Tipo = eContadores.NumeroTransito.ToString();
                        oNum3.ValorIni = oUlt.UltimoTransito == 0 ? oUlt.UltimoTransito : oUlt.UltimoTransito + 1;
                        oNum3.ValorFin = oUlt.UltimoTransito;
                        oNum3.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroTransito, oNum3);

                        Numeradores oNum4 = new Numeradores();
                        oNum4.Tipo = eContadores.NumeroPagoDiferido.ToString();
                        oNum4.ValorIni = oUlt.UltimoPagoDiferido == 0 ? oUlt.UltimoPagoDiferido : oUlt.UltimoPagoDiferido + 1;
                        oNum4.ValorFin = oUlt.UltimoPagoDiferido;
                        oNum4.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroPagoDiferido, oNum4);

                        Numeradores oNum5 = new Numeradores();
                        oNum5.Tipo = eContadores.NumeroTicketNoFiscal.ToString();
                        oNum5.ValorIni = oUlt.UltimoTicketNoFiscal == 0 ? oUlt.UltimoTicketNoFiscal : oUlt.UltimoTicketNoFiscal + 1;
                        oNum5.ValorFin = oUlt.UltimoTicketNoFiscal;
                        oNum5.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroTicketNoFiscal, oNum5);

                        Numeradores oNum6 = new Numeradores();
                        oNum6.Tipo = eContadores.NumeroTransitoEscape.ToString();
                        oNum6.ValorIni = oUlt.UltimoTransitoEscape == 0 ? oUlt.UltimoTransitoEscape : oUlt.UltimoTransitoEscape + 1;
                        oNum6.ValorFin = oUlt.UltimoTransitoEscape;
                        oNum6.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroTransitoEscape, oNum6);

                        Numeradores oNum7 = new Numeradores();
                        oNum7.Tipo = eContadores.NumeroDetraccion.ToString();
                        oNum7.ValorIni = oUlt.UltimaDetraccion == 0 ? oUlt.UltimaDetraccion : oUlt.UltimaDetraccion + 1;
                        oNum7.ValorFin = oUlt.UltimaDetraccion;
                        oNum7.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroDetraccion, oNum7);

                        Numeradores oNum8 = new Numeradores();
                        oNum8.Tipo = eContadores.NumeroFactura.ToString();
                        oNum8.ValorIni = oUlt.UltimaFactura == 0 ? oUlt.UltimaFactura : oUlt.UltimaFactura + 1;
                        oNum8.ValorFin = oUlt.UltimaFactura;
                        oNum8.UltimaFecha = DateTime.Now;
                        nums.Add(eContadores.NumeroFactura, oNum8);
                        #endregion

                        if (_numeracionVieja && !_localActualizada)
                        {
                            var query = context.Numeradores.Where(p => p.IDTurno == _turnoActual).ToList();

                            if (query != null)
                            {
                                foreach (Numeradores num in query)
                                {
                                    eContadores eClave;
                                    Enum.TryParse(num.Tipo, out eClave);

                                    if (!nums.ContainsKey(eClave))
                                    {
                                        num.ValorIni = 0;
                                        num.ValorFin = 0;
                                    }
                                    else
                                    {
                                        num.ValorIni = nums[eClave].ValorIni;
                                        num.ValorFin = nums[eClave].ValorFin;
                                    }
                                    num.UltimaFecha = DateTime.Now;
                                }

                                /*var datos = context.Datos_Numeraciones.Find(_turnoActual);
                                if (datos != null)
                                {
                                    datos.UltimaNum = DateTime.Now;
                                    datos.Estado = eEstadoNumeracion.NumeracionSinConfirmar.ToString();
                                }*/
                            }
                            else
                                respuesta.CodError = EnmErrorBaseDatos.Falla;
                        }
                        else
                        {
                            var lastOne = Enum.GetValues(typeof(eContadores)).Cast<eContadores>().Max();
                            var query = new List<Numeradores>();
                            if (_numeradorCorrupto)
                            {
                                query = context.Numeradores.Where(p => p.IDTurno == _turnoActual).ToList();
                            }

                            for (int i = 0; i <= (int)lastOne; i++)
                            {
                                Numeradores oNum1 = new Numeradores();
                                eContadores contador = (eContadores)i;

                                if (!nums.ContainsKey(contador))
                                {
                                    oNum1.Tipo = contador.ToString();
                                    oNum1.ValorIni = 0;
                                    oNum1.ValorFin = 0;
                                }
                                else
                                    oNum1 = nums[contador];

                                oNum1.IDTurno = _turnoActual;
                                oNum1.UltimaFecha = DateTime.Now;

                                //si en la BD local los numeradores estaban corruptos y ya existía alguno de ellos reeplazamos el valor
                                if(_numeradorCorrupto && query.Any(x => x.Tipo == oNum1.Tipo))
                                {
                                    //solo reemplazar el valor si la BD local esta desactualizada
                                    if (!_localActualizada)
                                    {
                                        int index = query.FindIndex(x => x.Tipo == contador.ToString());
                                        query[index].ValorIni = oNum1.ValorIni;
                                        query[index].ValorFin = oNum1.ValorFin;
                                        query[index].UltimaFecha = oNum1.UltimaFecha;
                                    }
                                }
                                else
                                    context.Numeradores.Add(oNum1);
                            }

                            //Almacena la fecha de ultima actualizacion y el estado actual
                            /*var buscar = context.Datos_Numeraciones.Find(_turnoActual);
                            if (buscar != null)
                            {
                                buscar.Estado = eEstadoNumeracion.NumeracionSinConfirmar.ToString();
                            }
                            else
                            {
                                DatosNumeraciones oDatos = new DatosNumeraciones();
                                oDatos.IDTurno = _turnoActual;
                                oDatos.UltimaNum = DateTime.Now;
                                oDatos.Estado = eEstadoNumeracion.NumeracionSinConfirmar.ToString();
                                context.Datos_Numeraciones.Add(oDatos);
                            }*/
                        }
                        _numeracionVieja = false;
                        _numeradorCorrupto = false;
                        _localActualizada = false;
                        break;
                    case eTablaBD.SetFechaNumerador:
                        var fechaN = context.Datos_Numeraciones.Find(_turnoActual);
                        if (fechaN != null)
                        {
                            fechaN.UltimaNum = DateTime.Now;
                        }
                        break;
                    case eTablaBD.SetEstadoNumerador:
                        var estado = context.Datos_Numeraciones.Find(_turnoActual);
                        if (estado != null)
                        {
                            estado.UltimaNum = DateTime.Now;
                            estado.Estado = solicitud.Filtro;
                        }
                        else
                        {
                            DatosNumeraciones oDatos = new DatosNumeraciones();
                            oDatos.IDTurno = _turnoActual;
                            oDatos.UltimaNum = DateTime.Now;
                            oDatos.Estado = solicitud.Filtro;
                            context.Datos_Numeraciones.Add(oDatos);
                        }
                        break;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = sRespuesta;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if(respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;
            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla transitos
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarTransito(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                Transitos traTur = new Transitos();

                try
                {
                    traTur = JsonConvert.DeserializeObject<Transitos>(solicitud.Filtro);
                }
                catch (JsonException e)
                {
                    _logger.Error("Excepcion: {0}", e.ToString());
                    respuesta.CodError = EnmErrorBaseDatos.Falla;
                    respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
                    return respuesta;
                }

                var tran = context.Transitos.Find(_turnoActual, traTur.TipOp, traTur.TipBo, traTur.SubFp,
                                                        traTur.ConAp, traTur.CatMan);

                if (tran == null)
                {
                    traTur.IDTurno = _turnoActual;
                    context.Transitos.Add(traTur);
                }
                else
                {
                    tran.CantiMan = tran.CantiMan + traTur.CantiMan;
                    tran.Monto = tran.Monto + traTur.Monto;
                    tran.CantiDac = tran.CantiDac + traTur.CantiDac;
                    tran.MontoDac = tran.MontoDac + traTur.MontoDac;
                    tran.CantiAnulado = tran.CantiAnulado + traTur.CantiAnulado;
                    tran.MontoAnulado = tran.MontoAnulado + traTur.MontoAnulado;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla anomalias
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarAnomalia(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                Anomalias anomTur = new Anomalias();

                anomTur = JsonConvert.DeserializeObject<Anomalias>(solicitud.Filtro);

                var anom = context.Anomalias.Find(_turnoActual, anomTur.CodAnom);

                if (anom == null)
                {
                    anomTur.IDTurno = _turnoActual;
                    context.Anomalias.Add(anomTur);
                }
                else
                {
                    anom.Cantidad = anom.Cantidad + anomTur.Cantidad;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla ventas
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarVenta(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                Ventas ventasTurno = new Ventas();

                ventasTurno = JsonConvert.DeserializeObject<Ventas>(solicitud.Filtro);

                var ventas = context.Ventas.Find(_turnoActual, ventasTurno.Tipo, ventasTurno.CatMan);

                if (ventas == null)
                {
                    ventasTurno.IDTurno = _turnoActual;
                    context.Ventas.Add(ventasTurno);
                }
                else
                {
                    ventas.CantiMan = ventas.CantiMan + ventasTurno.CantiMan;
                    ventas.Monto = ventas.Monto + ventasTurno.Monto;
                    ventas.CantiAnulado = ventas.CantiAnulado + ventasTurno.CantiAnulado;
                    ventas.MontoAnulado = ventas.MontoAnulado + ventasTurno.MontoAnulado;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información en la tabla otros montos
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarOtroMonto(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                OtrosMontos otrosTurno = new OtrosMontos();

                otrosTurno = JsonConvert.DeserializeObject<OtrosMontos>(solicitud.Filtro);

                var otros = context.OtrosMontos.Find(_turnoActual, otrosTurno.TipoTotal);

                if (otros == null)
                {
                    otrosTurno.IDTurno = _turnoActual;
                    context.OtrosMontos.Add(otrosTurno);
                }
                else
                {
                    otros.Cantidad = otros.Cantidad + otrosTurno.Cantidad;
                    otros.Valor = otros.Valor + otrosTurno.Valor;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Almacena información de la via
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos GuardarInfoVia(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                InformacionVias infoVia = new InformacionVias();
                infoVia = JsonConvert.DeserializeObject<InformacionVias>(solicitud.Filtro);
                var info = context.InformacionVias.AsNoTracking().FirstOrDefault();

                if (info == null)
                    context.InformacionVias.Add(infoVia);
                else
                {
                    context.InformacionVias.Attach(infoVia);
                    context.Entry(infoVia).State = EntityState.Modified;
                }

                context.SaveChanges();
                respuesta.RespuestaDB = "Se almacenó información del Turno: " + solicitud.Tabla;
            }
            catch (JsonException e)
            {
                _logger.Error("Excepcion {0}: {1}", solicitud.Tabla, e.Message);
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (DbEntityValidationException e)
            {
                _logger.Error("DbEntityValidationException al salvar los cambios del objeto. Detalle: {0}", ValidationException(e));
                respuesta.CodError = EnmErrorBaseDatos.Falla;
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                _logger.Error("Excepcion al salvar los cambios del objeto: [{0}]:{1}", e.Message, e.InnerException.ToString());
            }

            if (respuesta.CodError == EnmErrorBaseDatos.Falla)
                respuesta.RespuestaDB = "No se almacenó la información del Turno: " + solicitud.Tabla;

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Busca los registros deseados en la BD del turno
        /// </summary>
        /// <param name="solicitud"></param>
        /// <returns></returns>
        private RespuestaBaseDatos BuscarInfo(SolicitudBaseDatos solicitud)
        {
            _logger.Trace("Entro...");
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            string sJson = "";
            respuesta.CodError = EnmErrorBaseDatos.SinFalla;

            try
            {
                var context = new Turno();
                switch (solicitud.Tabla)
                {
                    case eTablaBD.GetInfo:
                        var query = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno).FirstOrDefault();

                        if (query != null)
                        {
                            _turnoActual = query.IDTurno > _turnoActual ? query.IDTurno : _turnoActual;
                            if (_turnoAnterior == 0)
                                _turnoAnterior = _turnoActual;
                            _numeroTurno = query.NumTurno;
                            sJson = JsonConvert.SerializeObject(query);
                        }
                        else
                            respuesta.CodError = EnmErrorBaseDatos.SinResultado;
                        break;
                    case eTablaBD.GetNumerador:
                        var queryGN = context.Numeradores.AsNoTracking().Where(p => p.IDTurno == _turnoActual).ToList();

                        if (queryGN.Any())
                            sJson = JsonConvert.SerializeObject(queryGN);
                        else
                            respuesta.CodError = EnmErrorBaseDatos.SinResultado;
                        break;
                    case eTablaBD.GetFechaNumerador:
                        var queryGFN = context.Numeradores.AsNoTracking()
                                                        .Where(p => p.IDTurno == _turnoActual && 
                                                                p.Tipo == solicitud.Filtro)
                                                        .Select(f => f.UltimaFecha)
                                                        .FirstOrDefault();

                        if (queryGFN != null && queryGFN != FECHA_MIN_SQL && queryGFN != DateTime.MinValue)
                            sJson = queryGFN.ToString();
                        else
                            respuesta.CodError = EnmErrorBaseDatos.SinResultado;
                        break;
                    case eTablaBD.GetEstadoNumerador:
                        var queryGEN = context.Datos_Numeraciones.AsNoTracking().Where(p => p.IDTurno == _turnoActual).Select(f => f.Estado).FirstOrDefault();

                        if (queryGEN != null)
                            sJson = queryGEN;
                        else
                            respuesta.CodError = EnmErrorBaseDatos.SinResultado;
                        break;
                    case eTablaBD.GetNumeroTurno:
                        var queryGNT = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno).Select(f => f.NumTurno).FirstOrDefault();

                        if (queryGNT > 0)
                        {
                            if (queryGNT == _numeroTurno && !_hayTurnoAbierto)
                                queryGNT++;

                            sJson = queryGNT.ToString();
                        }
                        else
                            respuesta.CodError = EnmErrorBaseDatos.SinResultado;
                        break;
                }
            }
            catch (Exception e)
            {
                respuesta.CodError = EnmErrorBaseDatos.Falla;
                sJson = "No se obtuvo la información del Turno: " + solicitud.Tabla.ToString();
                _logger.Error("Excepcion al obtener la informacion de {0}:{1}", solicitud.Tabla, e.Message);
            }

            respuesta.DescError = Utility.ObtenerDescripcionEnum(respuesta.CodError);
            respuesta.RespuestaDB = sJson;

            _logger.Trace("Salgo...");
            return respuesta;
        }

        /// <summary>
        /// Devuelve la excepcion con mas detalles
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private string ValidationException(DbEntityValidationException e)
        {
            _logger.Trace("Entro...");
            string sRet = "";

            foreach (var eve in e.EntityValidationErrors)
            {
                sRet += string.Format("Entity of type \"{0}\" in state \"{1}\" has the following validation errors:",
                    eve.Entry.Entity.GetType().Name, eve.Entry.State);
                foreach (var ve in eve.ValidationErrors)
                {
                    sRet += string.Format("- Property: \"{0}\", Error: \"{1}\"",
                        ve.PropertyName, ve.ErrorMessage);
                }
            }

            _logger.Trace("Salgo...");
            return sRet;
        }

        /// <summary>
        /// Obtiene los Transitos totales del turno de la via actual, u otra via
        /// de la misma instancia SQL
        /// </summary>
        /// <param name="bOtraVia">True si es otra via, de lo contrario False</param>
        /// <param name="sDatosVia">Numero de Via y Estacion, si se especifica otra vía</param>
        /// <returns>Totales de transito de la via y estación correspondiente</returns>
        private string GetTransitosTotales(bool bOtraVia, string sDatosVia)
        {
            _logger.Trace("Entro...");
            string sQueryTr = "", sQueryVentas = "", sQueryTurno = "", sRes = "", sJson = "", sConn = "", sIdturno = "";
            TotalesTransitoTurno oTotales = new TotalesTransitoTurno();

            #region Queries

            sQueryTurno = $@"SELECT Estacion,
                                    Via,
                                    NumTurno,
	                                FechaAper,
	                                FechaCierre,
	                                NroParte,
	                                Cajero
                             FROM Turnos
                             LEFT JOIN InformacionVias on 1=1
                             WHERE IDTurno = @numero
                             FOR JSON PATH";

            sQueryTr = $@"SELECT 
                                CASE 
			                        WHEN tipbo = 'U' THEN tipbo 
			                        ELSE tipop 
	                            END AS tipo, 
                                catman, 
                                CASE 
                                    WHEN cantiman = 0 THEN cantidac 
                                ELSE (cantiman-cantianulado) 
                                END AS total,
                                (monto-montoAnulado) as monto
                        FROM Transitos
                        WHERE tipop <> ' '
                        AND IDTurno = @numero                        
                        FOR JSON PATH";

            sQueryVentas = $@"SELECT  tipo,
	                                catman,
	                                (cantiman-cantianulado) as total,
	                                (monto-montoAnulado) as monto
	                          FROM Ventas
                              WHERE IDTurno = @numero
                              FOR JSON PATH";

            #endregion

            if (bOtraVia)
            {
                try
                {
                    sConn = OtraViaConnectionString(JsonConvert.DeserializeObject<ViaEstacion>(sDatosVia));
                    sIdturno = "(SELECT TOP 1 IDTurno FROM Transitos ORDER BY IDTurno DESC)"; //selecciona del ultimo turno
                }
                catch (Exception e)
                {
                    _logger.Error("Error al deserializar información de la otra vía: {0}...", e.ToString());
                    return "";
                }
                
            }
            else
            {
                sConn = _connectionString;
                sIdturno = _turnoTot.ToString();
            }

            sQueryTurno = sQueryTurno.Replace("@numero", sIdturno);
            sQueryTr = sQueryTr.Replace("@numero",sIdturno);
            sQueryVentas = sQueryVentas.Replace("@numero", sIdturno);

            //se obtiene info del turno
            EjecutaConsulta( sQueryTurno, ref sRes, sConn );

            sRes = sRes.Replace("[", "");
            sRes = sRes.Replace("]", "");

            oTotales = JsonConvert.DeserializeObject<TotalesTransitoTurno>(sRes);
            sRes = string.Empty;

            if (oTotales != null)
            {
                //se obtiene info de los totales de ese turno
                EjecutaConsulta(sQueryTr, ref sRes, sConn);

                oTotales.TotalesTr = JsonConvert.DeserializeObject<List<Totales>>(sRes);

                if( oTotales.TotalesTr == null )
                    oTotales.TotalesTr = new List<Totales>();

                sRes = string.Empty;

                //se obtiene info de los totales de ventas de ese turno
                EjecutaConsulta(sQueryVentas, ref sRes, sConn);

                oTotales.TotalesVn = JsonConvert.DeserializeObject<List<Totales>>(sRes);

                if( oTotales.TotalesVn == null )
                    oTotales.TotalesVn = new List<Totales>();

                sJson = JsonConvert.SerializeObject(oTotales);
            }

            _logger.Trace("Salgo...");
            return sJson;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Obtiene los XML generados por la BD para enviarlos a la aplicación de vía
        /// </summary>
        /// <param name="nAux">Turno actual</param>
        /// <returns>Json respuesta para la vía</returns>
        /// ************************************************************************************************
        private string GetTotales(int nAux)
        {
            _logger.Trace("Entro...");
            bool bRet;
            string sQuery, sRes = "", sJson = "";
            TotalesTurno oTotales = new TotalesTurno();

            //Transito
            sQuery = $"SELECT* FROM Transitos where IDTurno = {nAux} AND TipOp <> ' ' FOR XML AUTO";
            bRet = EjecutaConsulta(sQuery, ref sRes);
            oTotales.Transitos = "<Transitos>" + sRes + "</Transitos>";
            sRes = "";
            _logger.Debug("XML Transitos");

            //Anomalia
            sQuery = $"SELECT* FROM Anomalias where IDTurno = {nAux} FOR XML AUTO";
            bRet = EjecutaConsulta(sQuery, ref sRes);
            oTotales.Anomalias = "<Anomalias>" + sRes + "</Anomalias>";
            sRes = "";
            _logger.Debug("XML Anomalias");

            //Numerador
            sQuery = $"SELECT* FROM Numeradores where IDTurno = {nAux} AND ValorFin >= ValorIni AND Tipo <> '{eContadores.UltimoTimestamp.ToString()}' FOR XML AUTO";
            bRet = EjecutaConsulta(sQuery, ref sRes);
            oTotales.Numeradores = "<Numeradores>" + sRes + "</Numeradores>";
            sRes = "";
            _logger.Debug("XML Numeradores");

            //Venta
            sQuery = $"SELECT* FROM Ventas where IDTurno = {nAux} FOR XML AUTO";
            bRet = EjecutaConsulta(sQuery, ref sRes);
            oTotales.Ventas = "<Ventas>" + sRes + "</Ventas>";
            sRes = "";
            _logger.Debug("XML Ventas");

            //Otro
            sQuery = $"SELECT* FROM OtrosMontos where IDTurno = {nAux} FOR XML AUTO";
            bRet = EjecutaConsulta(sQuery, ref sRes);
            oTotales.OtrosMontos = "<OtrosMontos>" + sRes + "</OtrosMontos>";
            _logger.Debug("XML Otros");

            sJson = JsonConvert.SerializeObject(oTotales);

            _logger.Trace("Salgo...");
            return sJson;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Ejecuta consulta deseada en la BD local
        /// </summary>
        /// <param name="sConnectionString">String de la conexión</param>
        /// <param name="sConsulta">consulta a ejecutar</param>
        /// <param name="sRes">Posible respuesta</param>
        /// <returns></returns>
        /// ************************************************************************************************
        private bool EjecutaConsulta(string sConsulta, ref string sRes, string sConn = "")
        {
            _logger.Trace("Entro...");
            bool bRet = false;

            if (string.IsNullOrEmpty(sConn))
                sConn = _connectionString;

            try
            {
                using (SqlConnection connection = new SqlConnection(sConn))
                using (SqlCommand command = new SqlCommand(sConsulta, connection))
                {
                    command.CommandTimeout = 2;
                    //Establezco la conexión...
                    connection.Open();
                    try
                    {
                        using (SqlDataReader _reader = command.ExecuteReader())
                        {
                            if (_reader != null)
                            {
                                while (_reader.Read())
                                {
                                    sRes += _reader[0].ToString();
                                }
                                bRet = true;
                            }
                        }
                    }
                    catch (SqlException e)
                    {
                        _logger.Error("Excepcion Consulta. {0}:{1}", e.Number, e.Message);
                    }
                }
            }
            catch (SqlException e)
            {
                _logger.Error("Exception {0}:{1}.", e.ErrorCode, e.Message);
            }
            catch (Exception e)
            {
                _logger.Error("General Exception {0}.", e.ToString());
            }

            _logger.Trace("Salgo");
            return bRet;
        }

        /// <summary>
        /// Establece la connection string para extraer información de otra BD
        /// </summary>
        /// <param name="oViaEst">Via y estacion a consultar</param>
        /// <returns>Connection string de la BD de la via solicitada</returns>
        private string OtraViaConnectionString(ViaEstacion oViaEst)
        {
            string sDB = "", sServer = "", sConn = "";
            sDB = "Turno" + oViaEst.Via.PadLeft(3,'0') + oViaEst.Estacion.PadLeft(2, '0');
            sServer = Configuraciones.Instance.Configuracion.LocalPath;
            sConn = "Server=" + sServer + ";Database=" + sDB + ";User Id=sa;Password=TeleAdmin01;Connection Timeout=1";

            return sConn;
        }

        /// ************************************************************************************************
        /// <summary>
        /// Proporciona el numero de Turno actual
        /// </summary>
        /// <returns></returns>
        /// ************************************************************************************************
        public static int ObtenerNumeroTurno()
        {
            return _numeroTurno;
        }

        /// <summary>
        /// Setea la connection string
        /// </summary>
        /// <param name="sConnection">connection string</param>
        public static void SetConnectionString(string sConnection)
        {
            _connectionString = sConnection;
        }

        /// <summary>
        /// Realiza un chequeo del IDTurno actual y obtiene los datos necesarios
        /// </summary>
        public static void ChequearTurno()
        {
            _logger.Trace("Entro");
            //Chequea ID turno

            try
            {
                var context = new Turno();
                var query = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno)
                                                         .Select(p => new { p.IDTurno, p.NumTurno, p.TurnoAbierto })
                                                         .FirstOrDefault();

                if (query != null)
                {
                    _turnoActual = query.IDTurno;
                    _turnoAnterior = _turnoActual;
                    _numeroTurno = query.NumTurno;
                    _hayTurnoAbierto = query.TurnoAbierto == "S" ? true : false;
                    _hayNumeradoresTurno = _hayTurnoAbierto;

                    if (_hayTurnoAbierto)
                        _turnoTot = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno)
                                                                 .Where(b => b.TurnoAbierto == "N")
                                                                 .Select(p => p.IDTurno).FirstOrDefault();
                    else
                        _turnoTot = _turnoActual;

                }
            }
            catch(Exception e)
            {
                _logger.Error(e.ToString());
            }

            _logger.Trace("Salgo");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nMaximo"></param>
        /// <returns></returns>
        public static bool ChequearBorrarRegistros(int nMaximo)
        {
            _logger.Trace("Entro");
            bool bRet = false;

            try
            {
                var context = new Turno();
                //Cuenta registros de la tabla Turnos
                var query = context.Turnos.AsNoTracking().Count();

                if(query > nMaximo)
                {
                    //Obtengo ultimo IDTurno (el mayor)
                    var idTurno = context.Turnos.AsNoTracking().OrderByDescending(p => p.IDTurno).Select(f => f.IDTurno).FirstOrDefault();
                    //Obtengo todos los registros de todas las tablas menores a IDTurno para borrarlos
                    var deletTurnos = context.Turnos.Where(p => p.IDTurno < idTurno);
                    var deletNums = context.Numeradores.Where(p => p.IDTurno < idTurno);
                    var deletAnom = context.Anomalias.Where(p => p.IDTurno < idTurno);
                    var deletTrans = context.Transitos.Where(p => p.IDTurno < idTurno);
                    var deletVent = context.Ventas.Where(p => p.IDTurno < idTurno);
                    var deletOtro = context.OtrosMontos.Where(p => p.IDTurno < idTurno);

                    //Borro todos los registros de todas las tablas menores a ese IDTurno
                    context.Turnos.RemoveRange(deletTurnos);
                    context.Numeradores.RemoveRange(deletNums);
                    context.Anomalias.RemoveRange(deletAnom);
                    context.Transitos.RemoveRange(deletTrans);
                    context.Ventas.RemoveRange(deletVent);
                    context.OtrosMontos.RemoveRange(deletOtro);

                    //Guardo los cambios
                    context.SaveChanges();

                    bRet = true;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e.ToString());
            }

            _logger.Trace("Salgo");
            return bRet;
        }

        public void SetNumeradorCorrupto(bool valor)
        {
            _numeradorCorrupto = valor;
        }
    }
}
