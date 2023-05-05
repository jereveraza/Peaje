using System;
using System.Collections.Generic;
using Entidades.ComunicacionOCR;
using System.Globalization;
using Utiles;
using Entidades;
using Entidades.Logica;
using Comunicacion;
using Entidades.Comunicacion;
using Entidades.ComunicacionAntena;
using Entidades.ComunicacionBaseDatos;
using System.Timers;
using System.Linq;

namespace ModuloLogicaVia.Logica
{
    public class HabOcr
    {
        public ulong NroVehiculo { get; set; }
        public string Patente { get; set; }

        public HabOcr(ulong nNro, string sPatente)
        {
            NroVehiculo = nNro;
            Patente = sPatente;
        }
    }

    public partial class LogicaViaDinamica
    {
        private object _lockAsignarTagLeidoOCR = new object();

        private int _OCRAlturaAdelantado, _OCRDifConfiabilidad, _OCRTiempoLazo, _OcrCodigoPais, _OCRTiempoHabilita;
        private float _OCRMinLeviDist;
        private bool _bEsperandoTagPosOcr = false;
        private Timer _timerEsperaPosOcr = new Timer();
        private List<HabOcr> _lRevHabOcr = new List<HabOcr>();

        private void InitOCR()
        {
            _OCRAlturaAdelantado = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "ALTURA_ADELANTE"));
            _OCRDifConfiabilidad = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DIF_CONFIABILIDAD"));
            _OCRTiempoLazo = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "TIEMPO_LAZO"));
            _OcrCodigoPais = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "CodigoPais"));
            _OCRMinLeviDist = float.Parse(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DistMinLevi"), CultureInfo.InvariantCulture.NumberFormat);
            _OCRTiempoHabilita = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "SEG_ESPERA_HABILITA")) * 1000;

            _timerEsperaPosOcr.Elapsed += new ElapsedEventHandler(OnTimeOutEsperaPosOcr);
            _timerEsperaPosOcr.Interval = _OCRTiempoHabilita;
            _timerEsperaPosOcr.AutoReset = false;
            _timerEsperaPosOcr.Enabled = false;
        }

        private bool AsignarTagLeidoPorOCR(InfoTag oInfoTag, ulong NroVeh)
        {
            bool bRet = false, bEnc = false;
            Vehiculo oVeh = null;

            lock (_lockAsignarTagLeidoOCR)
            {
                _logger.Info("AsignarTagLeidoPorOCR -> Inicio. Tag [{0}]", oInfoTag.NumeroTag);

                bEnc = false;

                for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP1; i--)
                {
                    if (GetVehiculo((eVehiculo)i).NumeroVehiculo != 0 &&  //Tengo numero de vehiculo
                        !GetVehiculo((eVehiculo)i).EstaPagado)
                    {
                        NroVeh = GetVehiculo((eVehiculo)i).NumeroVehiculo;
                        _logger.Info("AsignarTagLeidoPorOCR -> Inicio Tag:[{0}] Numero Vehiculo[{1}]", oInfoTag.NumeroTag, NroVeh);
                        bEnc = true;
                        oVeh = GetVehiculo((eVehiculo)i);
                        break;
                    }
                }

                RegistroPagoTag(ref oInfoTag, ref oVeh);

                if (bEnc)
                {
                    //Asigno la fecha de pago con tag
                    oInfoTag.FechaPago = DateTime.Now;

                    _logger.Info("AsignarTagLeidoPorOCR -> Asigno tag. Tag[{0}], Vehículo [{1}]", oInfoTag.NumeroTag, NroVeh);

                    //Hay un solo vehículo, llamo a AsignarTagAVehiculo
                    AsignarTagAVehiculo(oInfoTag, NroVeh);

                    SetVehiculoIng(false, true, false, true);
                }

                _logger.Info("AsignarTagLeidoPorOCR -> Fin");
                LoguearColaVehiculos();
            }
            return bRet;
        }

        private void OnLecturaPatenteOCR(InfoOCR InfoOCR)
        {
            try
            {
                _logger.Info("OnLecturaPatenteOCR -> Inicio");
                _logger.Info("OnLecturaPatenteOCR -> Lectura OCR = Patente [{0}] Confiabilidad [{1}] Altura [{2}]", InfoOCR.Patente, InfoOCR.Confiabilidad, InfoOCR.Altura);

                bool bLecturaAdelante = InfoOCR.Altura >= _OCRAlturaAdelantado ? true : false;

                _logger.Info("OnLecturaPatenteOCR -> bLecturaAdelante[{0}]", bLecturaAdelante ? "S" : "N");

                double dist = 0.0;
                bool bEnc = false;
                for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP3 && !bEnc; i--)
                {
                    if (GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente != "")
                    {
                        //Calculamos la similitud
                        dist = ClassUtiles.DistanciaLevenshtein(GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente, InfoOCR.Patente);
                        _logger.Info("OnLecturaPatenteOCR -> Similitud entre: [{0}] y [{1}] es de: {2}", GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente, InfoOCR.Patente, dist);
                    }
                    else
                    {
                        dist = 0.0;
                    }

                    if (GetVehiculo((eVehiculo)i).NumeroVehiculo != 0 &&  //Tengo numero de vehiculo
                        (GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente == "" || dist <= _OCRMinLeviDist || InfoOCR.Patente.Length >= GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente.Length + 2) && //Solo si la patente es parecida o mucho ms larga
                        (!(GetVehiculo((eVehiculo)i).SalidaON && GetVehiculo((eVehiculo)i).GetSalidaONClock() < _OCRTiempoLazo) || bLecturaAdelante)        //aun no piso el lazo o se lee una patente muy grande
                        //solo si son similares
                        )
                    {
                        _logger.Info("OnLecturaPatenteOCR -> Usamos Vehiculo [{0}][{1}]", ((eVehiculo)i).ToString(), i);
                        _logger.Info("OnLecturaPatenteOCR -> NroVehiculo [{0}] Patente Actual [{1}] Conf Actual [{2}]", GetVehiculo((eVehiculo)i).NumeroVehiculo, GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente, GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad);

                        if (GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente == ""
                            || InfoOCR.Patente.Length > GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente.Length && InfoOCR.Confiabilidad > GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad - _OCRDifConfiabilidad
                            || InfoOCR.CodigoPais == _OcrCodigoPais && GetVehiculo((eVehiculo)i).InfoOCRDelantero.CodigoPais != _OcrCodigoPais && InfoOCR.Confiabilidad > GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad - _OCRDifConfiabilidad      // es de Uruguay y el anterior no
                            || InfoOCR.Patente.Length == GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente.Length && InfoOCR.Confiabilidad > GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad)//Si la conf nueva es mayor para mismo largo	
                        {
                            //Asigno el InfoOCR al vehiculo
                            GetVehiculo((eVehiculo)i).InfoOCRDelantero = InfoOCR;

                            //Actualizo la pantalla para mostar la patente solo si es el 1er vehiculo
                            if (GetPrimerVehiculo().NumeroVehiculo == GetVehiculo((eVehiculo)i).NumeroVehiculo)
                            {
                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(GetVehiculo((eVehiculo)i), ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.PATENTE_OCR, listaDatosVia);
                            }

                            InfoMedios oFoto = new InfoMedios(InfoOCR.NombreFoto, eCamara.Frontal, eTipoMedio.OCR, eCausaVideo.Nada, false);

                            GetVehiculo((eVehiculo)i).ListaInfoFoto?.Add(oFoto);
                            GetVehiculo((eVehiculo)i).InfoPagado.CargarValores(GetVehiculo((eVehiculo)i));
                            _logger.Trace("OnLecturaPatenteOCR -> Guardo Foto OCR: [{0}]", InfoOCR.NombreFoto);

                            _bEsperandoTagPosOcr = true;

                            //NO disparar timer
                            if (_logicaCobro.Estado != eEstadoVia.EVCerrada && !GetVehiculo((eVehiculo)i).EstaPagado && 
                                InfoOCR.Confiabilidad >= ModuloBaseDatos.Instance.ConfigVia.ConfiabilidadMinOCR)
                            {
                                //agregar lectura a la lista de pendientes por revisar habilitacion
                                HabOcr oHab = _lRevHabOcr.Where(x => x.NroVehiculo == GetVehiculo((eVehiculo)i).NumeroVehiculo).FirstOrDefault();
                                if (oHab != null)
                                    oHab.Patente = InfoOCR.Patente;
                                else
                                    _lRevHabOcr.Add(new HabOcr(GetVehiculo((eVehiculo)i).NumeroVehiculo, InfoOCR.Patente));

                                if (_timerEsperaPosOcr.Enabled)
                                    _timerEsperaPosOcr.Stop();

                                _timerEsperaPosOcr.Start();
                            }
                            else
                                _logger.Debug("OnLecturaPatenteOCR -> NO revisar habilitacion de OCR [{0}], EstadoVia [{1}] - VehiculoPagado? [{2}] - ConfiabilidadBaja? [{3}: {4}]",
                                    GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente,
                                    _logicaCobro.Estado.ToString(),
                                    GetVehiculo((eVehiculo)i).EstaPagado? "SI":"NO",
                                    GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad < ModuloBaseDatos.Instance.ConfigVia.ConfiabilidadMinOCR? "SI" : "NO",
                                    GetVehiculo((eVehiculo)i).InfoOCRDelantero.Confiabilidad);
                        }
                        else
                        {
                            _logger.Info("OnLecturaPatenteOCR -> Lectura nueva es menos confiable");
                        }
                    }
                    else
                    {
                        eVehiculo eVeh = (eVehiculo)i;
                        string sAux = string.Format("OnLecturaPatenteOCR -> T_Vehiculo [{0}] NroVeh [{1}] VehPatOcr [{2}] dist [{3}] OcrLength [{4}] VehOcrLength [{5}] SalOn [{6}] SalONClock[{7}] bLecturaAdelante[{8}]",
                        eVeh.ToString(),
                        GetVehiculo((eVehiculo)i).NumeroVehiculo,
                        GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente,
                        dist,
                        InfoOCR.Patente.Length,
                        GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente.Length,
                        GetVehiculo((eVehiculo)i).SalidaON ? "S" : "N",
                        GetVehiculo((eVehiculo)i).GetSalidaONClock(),
                        bLecturaAdelante ? "S" : "N");
                        _logger.Info(sAux);
                    }
                }

                _logger.Info("OnLecturaPatenteOCR -> Fin");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "OnLecturaPatenteOCR -> {0}", e.ToString());
            }
        }

        private void RevisarHabilitacionOCR()
        {
            try
            {
                _logger.Info("RevisarHabilitacionOCR -> Inicio");
                bool bEnc = false;
                eVehiculo eVeh = eVehiculo.eVehAnt;

                for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP3 && !bEnc; i--)
                {
                    if(GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente != "")
                    {
                        if (_lRevHabOcr != null && _lRevHabOcr.Count > 0)
                        {
                            HabOcr oHab = _lRevHabOcr.Where(x => x.NroVehiculo == GetVehiculo((eVehiculo)i).NumeroVehiculo &&
                                                    x.Patente == GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente).FirstOrDefault();

                            if(oHab != null)
                            {
                                eVeh = (eVehiculo)i;
                                bEnc = true;
                                _lRevHabOcr.Remove(oHab);
                            }
                        }
                    }
                }

                //no encontré nada, limpio la lista
                if (!bEnc)
                    _lRevHabOcr.Clear();

                if (bEnc && ModuloBaseDatos.Instance.ConfigVia.HabilitaPasoOCR == 'S' && 
                    (!(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)))
                {
                    _logger.Info("RevisarHabilitacionOCR -> OCR habilita pasada");

                    if (GetVehiculo(eVeh).InfoOCRDelantero.Confiabilidad >= ModuloBaseDatos.Instance.ConfigVia.ConfiabilidadMinOCR)
                    {
                        _logger.Info("RevisarHabilitacionOCR -> Se busca patente");

                        string sCausaValidacion;
                        Tag tag;
                        if (ValidarOCR(GetVehiculo(eVeh).InfoOCRDelantero.Patente, out tag, out sCausaValidacion))
                        {
                            _logger.Info("RevisarHabilitacionOCR -> NumTag [{0}] NumTID [{0}]", tag.NumeroTag, tag.NumeroTID);

                            if (!GetVehiculo(eVeh).EstaPagado && !GetVehiculo(eVeh).CobroEnCurso && !GetVehiculo(eVeh).EnOperacionManual)
                            {
                                //GetVehiculo(eVeh).LecturaPorOCR = true;
                                ProcesarLecturaTag(eEstadoAntena.Ok, tag, eTipoLecturaTag.OCR);
                            }
                            else
                            {
                                _logger.Info("RevisarHabilitacionOCR -> Vehiculo Pagado, o en proceso de cobro, no cobro por OCR");
                            }
                        }
                        else
                        {
                            _logger.Info("RevisarHabilitacionOCR -> No se cobra por OCR: " + sCausaValidacion);
                        }
                    }
                    else
                    {
                        _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por baja confiabilidad");
                    }
                }
                else
                {
                    _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por configuración");
                }

                //todavia tiene patentes pendientes
                if (_lRevHabOcr != null && _lRevHabOcr.Any())
                    _timerEsperaPosOcr.Start();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "RevisarHabilitacionOCR -> {0}", e.ToString());
            }
        }

        private bool ValidarOCR(string sPatenteOCR, out Tag tag, out string sCausa)
        {
            _logger.Trace("ValidarOCR -> Entro");
            bool bRet = true;
            sCausa = "";
            tag = new Tag();

            try
            {
                _logger.Info("ValidarOCR -> Inicio Patente [{0}]", sPatenteOCR);
                //Valido que exista la patente tabla local y tenga la habilitacion para trabajar por OCR
                TagBD oTag = ModuloBaseDatos.Instance.BuscarTagPorPatente(sPatenteOCR);

                if (oTag.EstadoConsulta == EnmStatusBD.OK)
                {
                    tag.NumeroTag = oTag.NumeroTag;
                    tag.NumeroTID = tag.NumeroTag;

                    if (oTag.HabilitadoOCR == 'S')
                    {
                        bRet = true;
                        sCausa = $"Tag {oTag.NumeroTag}";
                    }
                    else
                    {
                        bRet = false;
                        sCausa = "Patente no habilitada";
                    }
                }
                else
                {
                    bRet = false;
                    sCausa = "Patente no encontrada";
                }
                _logger.Info("ValidarOCR -> FIN está habilitada para OCR ? [{0}] {1}", bRet ? "Si" : "No", sCausa);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logger.Trace("ValidarOCR -> Salgo");

            return bRet;
        }

        #region Timers y Eventos
        private void OnTimeOutEsperaPosOcr(Object source, ElapsedEventArgs e)
        {
            _timerEsperaPosOcr.Stop();

            if(_bEsperandoTagPosOcr)
            {
                _bEsperandoTagPosOcr = false;
                RevisarHabilitacionOCR();
            }
        }
        #endregion
    }

    public partial class LogicaViaManual
    {
        private object _lockAsignarTagLeidoOCR = new object();

        private int _OCRAlturaAdelantado, _OCRDifConfiabilidad, _OCRTiempoLazo, _OcrCodigoPais, _OCRTiempoHabilita;
        private float _OCRMinLeviDist;
        private bool _bEsperandoTagPosOcr = false;
        private Timer _timerEsperaPosOcr = new Timer();
        private List<HabOcr> _lRevHabOcr = new List<HabOcr>();

        private void InitOCR()
        {
            _OCRAlturaAdelantado = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "ALTURA_ADELANTE"));
            _OCRDifConfiabilidad = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DIF_CONFIABILIDAD"));
            _OCRTiempoLazo = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "TIEMPO_LAZO"));
            _OcrCodigoPais = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "CodigoPais"));
            _OCRMinLeviDist = float.Parse(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DistMinLevi"), CultureInfo.InvariantCulture.NumberFormat);
            _OCRTiempoHabilita = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "SEG_ESPERA_HABILITA")) * 1000;

            _timerEsperaPosOcr.Elapsed += new ElapsedEventHandler(OnTimeOutEsperaPosOcr);
            _timerEsperaPosOcr.Interval = _OCRTiempoHabilita;
            _timerEsperaPosOcr.AutoReset = false;
            _timerEsperaPosOcr.Enabled = false;
        }

        private bool AsignarTagLeidoPorOCR(InfoTag oInfoTag, ulong NroVeh)
        {
            bool bRet = false, bEnc = false;
            Vehiculo oVeh = GetVehIng();

            lock (_lockAsignarTagLeidoOCR)
            {
                _logger.Info("AsignarTagLeidoPorOCR -> Inicio. Tag [{0}]", oInfoTag.NumeroTag);

                bEnc = false;

                for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP1; i--)
                {
                    if (GetVehIng().NumeroVehiculo != 0 &&  //Tengo numero de vehiculo
                        !GetVehIng().EstaPagado)
                    {
                        NroVeh = GetVehIng().NumeroVehiculo;
                        _logger.Info("AsignarTagLeidoPorOCR -> Inicio Tag:[{0}] Numero Vehiculo[{1}]", oInfoTag.NumeroTag, NroVeh);
                        bEnc = true;
                        oVeh = GetVehIng();
                        break;
                    }
                }

                RegistroPagoTag(ref oInfoTag, ref oVeh);

                if (bEnc)
                {
                    //Asigno la fecha de pago con tag
                    oInfoTag.FechaPago = DateTime.Now;

                    _logger.Info("AsignarTagLeidoPorOCR -> Asigno tag. Tag[{0}], Vehículo [{1}]", oInfoTag.NumeroTag, NroVeh);

                    //Hay un solo vehículo, llamo a AsignarTagAVehiculo
                    AsignarTagAVehiculo(oInfoTag, NroVeh);
                }

                _logger.Info("AsignarTagLeidoPorOCR -> Fin");
                LoguearColaVehiculos();
            }
            return bRet;
        }

        private void OnLecturaPatenteOCR(InfoOCR InfoOCR)
        {
            try
            {
                _logger.Info("OnLecturaPatenteOCR -> Inicio");
                _logger.Info("OnLecturaPatenteOCR -> Lectura OCR = Patente [{0}] Confiabilidad [{1}] Altura [{2}]", InfoOCR.Patente, InfoOCR.Confiabilidad, InfoOCR.Altura);

                bool bLecturaAdelante = InfoOCR.Altura >= _OCRAlturaAdelantado ? true : false;

                _logger.Info("OnLecturaPatenteOCR -> bLecturaAdelante[{0}]", bLecturaAdelante ? "S" : "N");

                double dist = 0.0;
                bool bEnc = false;
                Vehiculo vehiculo = GetVehIng();
                if (vehiculo.NumeroVehiculo == 0)
                    vehiculo.NumeroVehiculo = GetNextNroVehiculo();

                if (vehiculo.InfoOCRDelantero.Patente != "" )
                {
                    dist = ClassUtiles.DistanciaLevenshtein(vehiculo.InfoOCRDelantero.Patente, InfoOCR.Patente);
                }
                else
                    dist = 0.0;

                if (vehiculo.NumeroVehiculo != 0 &&  //Tengo numero de vehiculo
                        (vehiculo.InfoOCRDelantero.Patente == "" || dist <= _OCRMinLeviDist || InfoOCR.Patente.Length >= vehiculo.InfoOCRDelantero.Patente.Length + 2) && //Solo si la patente es parecida o mucho ms larga
                        (!vehiculo.SalidaON && vehiculo.GetSalidaONClock() < _OCRTiempoLazo || bLecturaAdelante))        //aun no piso el lazo o se lee una patente muy grande
                                                                                                                         //solo si son similares
                {
                    if (vehiculo.InfoOCRDelantero.Patente == ""
                            || InfoOCR.Patente.Length > vehiculo.InfoOCRDelantero.Patente.Length && InfoOCR.Confiabilidad > vehiculo.InfoOCRDelantero.Confiabilidad - _OCRDifConfiabilidad
                            || InfoOCR.CodigoPais == _OcrCodigoPais && vehiculo.InfoOCRDelantero.CodigoPais != _OcrCodigoPais && InfoOCR.Confiabilidad > vehiculo.InfoOCRDelantero.Confiabilidad - _OCRDifConfiabilidad      // es de Uruguay y el anterior no
                            || InfoOCR.Patente.Length == vehiculo.InfoOCRDelantero.Patente.Length && InfoOCR.Confiabilidad > vehiculo.InfoOCRDelantero.Confiabilidad)//Si la conf nueva es mayor para mismo largo	
                    {
                        //Asigno el InfoOCR al vehiculo
                        vehiculo.InfoOCRDelantero = InfoOCR;
                        vehiculo.InfoOCRTrasero = InfoOCR;
                        //Actualizo la pantalla para mostar la patente solo si es el 1er vehiculo
                        if (GetPrimerVehiculo().NumeroVehiculo == vehiculo.NumeroVehiculo)
                        {
                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.PATENTE_OCR, listaDatosVia);
                        }

                        InfoMedios oFoto = new InfoMedios(InfoOCR.NombreFoto, eCamara.Frontal, eTipoMedio.OCR, eCausaVideo.Nada, false);

                        vehiculo.ListaInfoFoto?.Add(oFoto);

                        if (ModuloBaseDatos.Instance.ConfigVia.HabilitaPasoOCR == 'S' &&
                        (!(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)))
                        {
                            _logger.Info("RevisarHabilitacionOCR -> OCR habilita pasada");

                            if (vehiculo.InfoOCRDelantero.Confiabilidad >= ModuloBaseDatos.Instance.ConfigVia.ConfiabilidadMinOCR)
                            {
                                _logger.Info("RevisarHabilitacionOCR -> Se busca patente");

                                string sCausaValidacion;
                                Tag tag;
                                if (ValidarOCR(vehiculo.InfoOCRDelantero.Patente, out tag, out sCausaValidacion))
                                {
                                    _logger.Info("RevisarHabilitacionOCR -> NumTag [{0}] NumTID [{0}]", tag.NumeroTag, tag.NumeroTID);

                                    if (!vehiculo.EstaPagado && !vehiculo.CobroEnCurso && !vehiculo.EnOperacionManual)
                                    {
                                        //GetVehiculo(eVeh).LecturaPorOCR = true;
                                        vehiculo.LecturaPorOCR = true;
                                        ProcesarLecturaTag(eEstadoAntena.Ok, tag, eTipoLecturaTag.OCR);
                                    }
                                    else
                                    {
                                        _logger.Info("RevisarHabilitacionOCR -> Vehiculo Pagado, o en proceso de cobro, no cobro por OCR");
                                    }
                                }
                                else
                                {
                                    _logger.Info("RevisarHabilitacionOCR -> No se cobra por OCR: " + sCausaValidacion);
                                }
                            }
                            else
                            {
                                _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por baja confiabilidad");
                            }
                        }
                        else
                        {
                            _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por configuración");
                        }


                    }

                }
                

                _logger.Info("OnLecturaPatenteOCR -> Fin");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "OnLecturaPatenteOCR -> {0}", e.ToString());
            }
        }

        private void RevisarHabilitacionOCR()
        {
            try
            {
                _logger.Info("RevisarHabilitacionOCR -> Inicio");
                bool bEnc = false;
                eVehiculo eVeh = eVehiculo.eVehAnt;

                for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP3 && !bEnc; i--)
                {
                    if (GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente != "")
                    {
                        if (_lRevHabOcr != null && _lRevHabOcr.Count > 0)
                        {
                            HabOcr oHab = _lRevHabOcr.Where(x => x.NroVehiculo == GetVehiculo((eVehiculo)i).NumeroVehiculo &&
                                                    x.Patente == GetVehiculo((eVehiculo)i).InfoOCRDelantero.Patente).FirstOrDefault();

                            if (oHab != null)
                            {
                                eVeh = (eVehiculo)i;
                                bEnc = true;
                                _lRevHabOcr.Remove(oHab);
                            }
                        }
                    }
                }

                //no encontré nada, limpio la lista
                if (!bEnc)
                    _lRevHabOcr.Clear();

                if (bEnc && ModuloBaseDatos.Instance.ConfigVia.HabilitaPasoOCR == 'S' &&
                    (!(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)))
                {
                    _logger.Info("RevisarHabilitacionOCR -> OCR habilita pasada");

                    if (GetVehiculo(eVeh).InfoOCRDelantero.Confiabilidad >= ModuloBaseDatos.Instance.ConfigVia.ConfiabilidadMinOCR)
                    {
                        _logger.Info("RevisarHabilitacionOCR -> Se busca patente");

                        string sCausaValidacion;
                        Tag tag;
                        if (ValidarOCR(GetVehiculo(eVeh).InfoOCRDelantero.Patente, out tag, out sCausaValidacion))
                        {
                            _logger.Info("RevisarHabilitacionOCR -> NumTag [{0}] NumTID [{0}]", tag.NumeroTag, tag.NumeroTID);

                            if (!GetVehiculo(eVeh).EstaPagado && !GetVehiculo(eVeh).CobroEnCurso && !GetVehiculo(eVeh).EnOperacionManual)
                            {
                                //GetVehiculo(eVeh).LecturaPorOCR = true;
                                ProcesarLecturaTag(eEstadoAntena.Ok, tag, eTipoLecturaTag.OCR);
                            }
                            else
                            {
                                _logger.Info("RevisarHabilitacionOCR -> Vehiculo Pagado, o en proceso de cobro, no cobro por OCR");
                            }
                        }
                        else
                        {
                            _logger.Info("RevisarHabilitacionOCR -> No se cobra por OCR: " + sCausaValidacion);
                        }
                    }
                    else
                    {
                        _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por baja confiabilidad");
                    }
                }
                else
                {
                    _logger.Info("RevisarHabilitacionOCR -> No se habilita OCR por configuración");
                }

                //todavia tiene patentes pendientes
                if (_lRevHabOcr != null && _lRevHabOcr.Any())
                    _timerEsperaPosOcr.Start();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "RevisarHabilitacionOCR -> {0}", e.ToString());
            }
        }

        private bool ValidarOCR(string sPatenteOCR, out Tag tag, out string sCausa)
        {
            _logger.Trace("ValidarOCR -> Entro");
            bool bRet = true;
            sCausa = "";
            tag = new Tag();

            try
            {
                _logger.Info("ValidarOCR -> Inicio Patente [{0}]", sPatenteOCR);
                //Valido que exista la patente tabla local y tenga la habilitacion para trabajar por OCR
                TagBD oTag = ModuloBaseDatos.Instance.BuscarTagPorPatente(sPatenteOCR);

                if (oTag.EstadoConsulta == EnmStatusBD.OK)
                {
                    tag.NumeroTag = oTag.NumeroTag;
                    tag.NumeroTID = tag.NumeroTag;

                    if (oTag.HabilitadoOCR == 'S')
                    {
                        bRet = true;
                        sCausa = $"Tag {oTag.NumeroTag}";
                    }
                    else
                    {
                        bRet = false;
                        sCausa = "Patente no habilitada";
                    }
                }
                else
                {
                    bRet = false;
                    sCausa = "Patente no encontrada";
                }
                _logger.Info("ValidarOCR -> FIN está habilitada para OCR ? [{0}] {1}", bRet ? "Si" : "No", sCausa);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logger.Trace("ValidarOCR -> Salgo");

            return bRet;
        }

        #region Timers y Eventos
        private void OnTimeOutEsperaPosOcr(Object source, ElapsedEventArgs e)
        {
            _timerEsperaPosOcr.Stop();

            if (_bEsperandoTagPosOcr)
            {
                _bEsperandoTagPosOcr = false;
                RevisarHabilitacionOCR();
            }
        }
        #endregion
    }
}
