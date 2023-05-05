using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entidades.Interfaces;
using Entidades.Logica;
using ModuloDAC_PLACAIO.Señales;
using Entidades;
using Comunicacion;
using Utiles;
using ModuloDAC_PLACAIO.DAC;
using ModuloDAC_PLACAIO.Mensajes;
using Entidades.ComunicacionBaseDatos;
using System.Diagnostics;
using Alarmas;
using Entidades.ComunicacionAntena;
using Entidades.ComunicacionVideo;
using Entidades.ComunicacionFoto;
using Entidades.ComunicacionEventos;
using Entidades.Comunicacion;
using System.Timers;

namespace ModuloLogicaVia.Logica
{
    public partial class LogicaViaManual : LogicaVia
    {
        #region CONSTANTES

        const int DEF_TIMEOUT_TAG_SINVEHICULO = 2000;
        #endregion

        #region VARIABLES PRIVADAS
        private ILogicaCobro _logicaCobro = null;
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger("LogicaVia");
        private static readonly NLog.Logger _loggerSensores = NLog.LogManager.GetLogger("Sensores");
        private static readonly NLog.Logger _loggerTiempos = NLog.LogManager.GetLogger("Tiempos");
        private NLog.Logger _loggerTransitos = NLog.LogManager.GetLogger("Transitos");
        private NLog.Logger _loggerExcepciones = NLog.LogManager.GetLogger("excepciones");

        private bool _bEnLazoPresencia = false;
        private bool _bUltimoSentidoEsOpuesto = false;
        private int _nComitivaVehPendientes = 0;
        private bool _tengoTagEnProceso = false;
        private bool _sentidoOpuesto = false;
        private bool _bMismoTag = false;
        private bool _bInitAntenaOK = true;
        private bool _bEnLazoSal = false;
        private bool _habiCatego = false;
        private bool _flagCatego = false;
        private bool _bStatusBarreraQ = false;

        private System.Timers.Timer _timerApagadoCampana = new System.Timers.Timer();

        private System.Timers.Timer _timerFotoLazoSalida = new System.Timers.Timer();

        private Vehiculo[] _vVehiculo = new Vehiculo[Vehiculo.GetMaxVehVector()];
        private bool _enSeparSal = false;
        private bool _enLazoSal = false;
        private bool _ultimoSentidoEsOpuesto = false;
        private int _comitivaVehPendientes = 0;
        private DateTime _tiempoDesactivacionAntena = DateTime.MinValue;
        private TimeSpan _tiempoDesactAnt = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(ClassUtiles.LeerConfiguracion("TAG", "TIEMPO_DESACT_ANT")));
        private TimeSpan _tiempoDesactAntBPI = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(ClassUtiles.LeerConfiguracion("TAG", "TIEMPO_DESAC_ANT_BPI")));
        private DateTime _fecUltimoTransito;

        //TODO Esto se tiene que obtener de logicaCobro
        public bool TicketEnProceso { get; set; }
        private bool _falloAltura = false;  //Indica si al momento de pagar el sensor de altura estaba en falla

        private char _estadoAbort = ' ';
        private bool _salidaSinTicket;
        private string _huellaDAC = "";
        private string _ultTagErrorTag = "";
        private bool _sinPeanas = false;
        private int _contSuspensionTurno;
        private const short _catego_Autotabular = 2;
        private InfoTagLeido _infoTagLeido = new InfoTagLeido();

        public bool _flagIniViol { get; private set; }


        #endregion


        public LogicaViaManual()
        {
            //Limpio todo
            MessageQueue.Instance.InicioColaOn = null;
            MessageQueue.Instance.InicioColaOff = null;
            MessageQueue.Instance.LazoPresenciaIngreso = null;
            MessageQueue.Instance.LazoPresenciaEgreso = null;
            MessageQueue.Instance.LazoSalidaIngreso = null;
            MessageQueue.Instance.LazoSalidaEgreso = null;
            MessageQueue.Instance.SeparadorSalidaIngreso = null;
            MessageQueue.Instance.SeparadorSalidaEgreso = null;

            //Sobrecargo con lo mio
            MessageQueue.Instance.InicioColaOn += InicioColaOn;
            MessageQueue.Instance.InicioColaOff += InicioColaOff;
            MessageQueue.Instance.LazoPresenciaIngreso += LazoPresenciaIngreso;
            MessageQueue.Instance.LazoPresenciaEgreso += LazoPresenciaEgreso;
            MessageQueue.Instance.LazoSalidaIngreso += LazoSalidaIngreso;
            MessageQueue.Instance.LazoSalidaEgreso += LazoSalidaEgreso;
            MessageQueue.Instance.SeparadorSalidaIngreso += SeparadorSalidaIngreso;
            MessageQueue.Instance.SeparadorSalidaEgreso += SeparadorSalidaEgreso;

            //Seteo la configuración en el pic
            DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.Manual);

            ModuloAntena.Instance.ProcesarLecturaTag += ProcesarLecturaTag;
            //TODO Sacar esto 
            ModuloVideo.Instance.ProcesarVideo += ProcesarComandoVideo;
            ModuloFoto.Instance.ProcesarFoto += ProcesarComandoFoto;
            ModuloOCR.Instance.ProcesarLecturaOCR += OnLecturaPatenteOCR;

            for (int i = 0; i < Vehiculo.GetMaxVehVector(); i++)
                _vVehiculo[i] = new Vehiculo();

            _OCRAlturaAdelantado = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "ALTURA_ADELANTE"));
            _OCRDifConfiabilidad = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DIF_CONFIABILIDAD"));
            _OCRTiempoLazo = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "TIEMPO_LAZO"));
            _OcrCodigoPais = Convert.ToInt16(ClassUtiles.LeerConfiguracion("MODULO_OCR", "CodigoPais"));
            _OCRMinLeviDist = Convert.ToSingle(ClassUtiles.LeerConfiguracion("MODULO_OCR", "DistMinLevi"));

            //Consulto el pic por las violaciones via apagada
            OnViolacionViaApagada();
        }

        override public bool UltimoSentidoEsOpuesto { get { return _ultimoSentidoEsOpuesto; } set { _ultimoSentidoEsOpuesto = value; } }
        override public int ComitivaVehPendientes { get { return _comitivaVehPendientes; } set { _comitivaVehPendientes = value; } }


        public override bool AsignandoTagAVehiculo
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override void DesactivarAntena(eCausaDesactivacionAntena causa)
        {
            //TODO IMPLEMENTAR
        }

        public override void GrabarVehiculos()
        {

        }

        override public void LazoEscapeIngreso()
        {

        }

        override public void LazoEscapeEgreso()
        {

        }

        override public void PulsadorEscape(short sEstado, bool bFromSupervision)
        {

        }

        override public void CapturaVideoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor)
        {

        }

        public override void CapturaFotoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor)
        {

        }

        public override eVehiculo BuscarVehiculo(ulong ulNroVehiculo, bool buscarNoPagado = false, bool buscarHastaC0 = false, string sNroTag = "")
        {
            return eVehiculo.eVehIng;
        }


        /// <summary>
        /// Devuelve si el bucle de salida está ocupado 
        /// depende del modo
        /// En Modo D o SM solo mira el BPA
        /// </summary>
        /// <returns>true si el bucle está ocupado</returns>
        override public bool EstaOcupadoBucleSalida()
        {
            byte sValor = 0;
            short ret = 0;
            bool respuesta = false;
            DAC_PlacaIO.Instance.EstaOcupadoBucleSalida(ref ret, ref sValor);

            //if (GetModelo() == "D" || m_bSinPeanas)
            respuesta = (sValor & ((int)eSensoresDAC.DEF_BPA_BYTE >> 4)) > 0 ? true : false;
            //else
            //    return Boolize(sValor & ((DEF_BPA_BYTE | DEF_BPA2_BYTE) >> 4));
            return respuesta;
        }

        public override bool EstaOcupadoLazoSalida()
        {
            return _enLazoSal;
        }

        public override bool EstaOcupadoSeparadorSalida()
        {
            return _enSeparSal;
        }

        public override void LimpiarTagsLeidos()
        {

        }

        public override Vehiculo GetVehiculo(eVehiculo eVehiculo)
        {
            //return m_vVehiculo[(int)eVehiculo];	
            return GetVehiculo((byte)eVehiculo);
        }
        public Vehiculo GetVehiculo(byte btIndex)
        {
            return _vVehiculo[btIndex];
        }

        public override Vehiculo GetVehAnt()
        {
            return _vVehiculo[(int)eVehiculo.eVehAnt];
        }

        public override Vehiculo GetVehEscape()
        {
            return _vVehiculo[(int)eVehiculo.eVehEscape];
        }

        public override bool GetHayVehiculosPagados()
        {
            //Si VehTran esta pagado o VehIng esta pagado devolvemos true
            if (GetVehTran().EstaPagado || GetVehIng().EstaPagado)
                return true;
            return false;
        }

        public override Vehiculo GetPrimerVehiculo()
        {
            //Si VehTran no es vacio retornamos VehTran, sino VehIng
            if (GetVehTran().NoVacio)
                return GetVehTran();
            else
                return GetVehIng();

        }

        public override Vehiculo GetPrimeroSegundoVehiculo(out bool esPrimero)
        {
            //Si VehTran es no vacio y no esta sobre el lazo retornamos VehTran, sino VehIng
            //VehTran siempre ya esta pagado
            esPrimero = true;
            if (GetVehTran().NoVacio && !GetVehTran().SalidaON)
                return GetVehTran();
            else
            {
                esPrimero = false;
                return GetVehIng();
            }
            //throw new NotImplementedException();
        }

        public override Vehiculo GetVehiculoAnterior()
        {
            return GetVehAnt();
            //throw new NotImplementedException();
        }

        public override Vehiculo GetVehIng(bool bSinPagadosEnBPA = false, bool bSinBPA = false, bool bSinPagados = false)
        {
            return _vVehiculo[(int)eVehiculo.eVehIng];
        }

        public override Vehiculo GetVehIngCat()
        {
            return GetVehIng(false, false, true);
        }

        override public Vehiculo GetVehOnline()
        {
            return _vVehiculo[(int)eVehiculo.eVehOnLine];
        }
        override public Vehiculo GetVehVideo()
        {
            return _vVehiculo[(int)eVehiculo.eVehVideo];
        }
        override public Vehiculo GetVehObservado()
        {
            return _vVehiculo[(int)eVehiculo.eVehObservado];
        }
        override public void LimpiarVehObservado()
        {
            _vVehiculo[(int)eVehiculo.eVehObservado] = new Vehiculo();
        }
        override public void LimpiarVeh(eVehiculo eVeh)
        {
            _vVehiculo[(int)eVeh] = new Vehiculo();
        }
        public override Vehiculo GetVehTran()
        {
            return _vVehiculo[(int)eVehiculo.eVehTra];
        }

        public override void LimpiarColaVehiculos()
        {
            foreach (eVehiculo vehiculo in Enum.GetValues(typeof(eVehiculo)))
            {
                _vVehiculo[(int)vehiculo] = new Vehiculo();
            }
        }

        /// <summary>
        /// Mensaje de salida de un vehículo al separador vehicular de entrada
        ///         '1'	quitar primero precola
        ///			'2'	quitar segundo precola
        ///			'3' quitar tercero precola
        ///			'0' limpiar la cola hacia adelante y precola hacia atras
        ///			'F' limpiar cola y precola hacia atras
        ///			'C' Retroceder todos los vehiculos una posicion
        /// </summary>
        /// <param name="esModoForzado"></param>
        /// <param name="RespDAC">respuesta del DAC</param>
        override public void InicioColaOff(bool esModoForzado, short RespDAC)
        {

            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';

            _logger.Info("******************* InicioColaOff -> Ingreso");
            //No hago nada en Modo Manual
            LoguearColaVehiculos();
            _logger.Info("******************* InicioColaOff -> Salida");
        }


        public override void InicioColaOn(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();


            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';

            _logger.Info("******************* InicioColaOn -> Ingreso [{Name}]", respuestaDAC);
            try
            {
                //En el modelo MD se activa al ocupar el BPI para sacar la foto
                //if (GetModelo() == "MD") //Para modelo M no tiene que sacar la foto
                //    DecideCaptura(DEF_VIDEO_ACC_BPI_OCUPAR);

                //Marcamos al vehiculo Ing que desactivamos la antena
                //Si VehTran ya tiene el SalidaON lo seteamos en VehIng
                //sino en VehTran
                if (GetVehTran().NoVacio)
                {
                    if (GetVehTran().SalidaON)
                    {
                        _logger.Info("Hace SetTiempoDesactivarAntena sobre VehIng");
                        GetVehIng().TiempoDesactivarAntena = DateTime.Now;
                    }
                    else
                    {
                        _logger.Info("Hace SetTiempoDesactivarAntena sobre VehTran");
                        GetVehTran().TiempoDesactivarAntena = DateTime.Now;
                    }
                }
                else
                {
                    _logger.Info("Hace SetTiempoDesactivarAntena sobre VehIng");
                    GetVehIng().TiempoDesactivarAntena = DateTime.Now;
                }

            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }


            LoguearColaVehiculos();
            _logger.Info("******************* InicioColaOn -> Salida");
        }

        public override void LazoPresenciaEgreso(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();

            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';

            _logger.Info("******************* LazoPresenciaEgreso -> Ingreso[{Name}]", respuestaDAC);
            try
            {
                _bEnLazoPresencia = false;

                // No se hace ni estuvo abierta en sentido opuesto
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(false))
                {
                    _logger.Info("Desactivo Antena");
                    DesactivarAntenaTimer(DAC_PlacaIO.Instance.EstaOcupadoBuclePresencia()); //Arranca un timer diferenciado por modelo de vía
                                                                                             //Lo hacemos por las dudas, por si el BPI no funciona

                    //Marcamos al vehiculo Ing que desactivamos la antena
                    //Si VehTran ya tiene el SalidaON lo seteamos en VehIng
                    //sino en VehTran
                    if (GetVehTran().NoVacio)
                    {
                        if (GetVehTran().SalidaON)
                        {
                            _logger.Info("Hace SetTiempoDesactivarAntena sobre VehIng");
                            GetVehIng().TiempoDesactivarAntena = DateTime.Now;
                        }
                        else
                        {
                            _logger.Info("Hace SetTiempoDesactivarAntena sobre VehTran");
                            GetVehTran().TiempoDesactivarAntena = DateTime.Now;
                        }
                    }
                    else
                    {
                        _logger.Info("Hace SetTiempoDesactivarAntena sobre VehIng");
                        GetVehIng().TiempoDesactivarAntena = DateTime.Now;
                    }


                    DecideCaptura(eCausaVideo.LazoPresenciaDesocupado);
                }
                else
                {
                    _logger.Debug("Sentido opuesto abierto");
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            LoguearColaVehiculos();
            _logger.Info("******************* LazoPresenciaEgreso -> Salida");
        }

        public override bool GetUltimoSentidoEsOpuesto()
        {
            return _bUltimoSentidoEsOpuesto;
        }

        public override int GetComitivaVehPendientes()
        {
            return _nComitivaVehPendientes;
        }

        private void DesactivarAntenaTimer(bool bPorSiguiente)
        {
            _logger.Debug("DesactivarAntenaTimer -> Inicio");


            TimeSpan tiempo;

            if (bPorSiguiente)
                tiempo = _tiempoDesactAntBPI; //modelo "D" por BPR, 1000mS por defecto
            else
                tiempo = _tiempoDesactAnt; //modelo "D" por liberar Separador, 100mS por defecto
            /*
             * TODO IMPLEMENTAR
            _tiempoDesactivacionAntena = DateTime.Now + tiempo;

            _timerDesactivarAntena.Interval = tiempo.TotalMilliseconds + 10;
            _timerDesactivarAntena.Start();
           
            _logger.Debug("DesactivarAntenaTimer -> Fin Interval[{name}]", _timerDesactivarAntena.Interval);
            */
        }


        public override void LoguearColaVehiculos()
        {

            StringBuilder sb = new StringBuilder();

            foreach (eVehiculo vehiculo in Enum.GetValues(typeof(eVehiculo)))
            {
                sb.Append($"Vehículo[{vehiculo.ToString().PadLeft(10)}] - Numero[{_vVehiculo[(int)vehiculo].NumeroVehiculo}] - CategoriaDAC[{_vVehiculo[(int)vehiculo].InfoDac.Categoria}]\n");
            }

            _logger.Debug(sb.ToString());


        }
        public override void LazoPresenciaIngreso(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();
            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';

            _logger.Info("******************* LazoPresenciaIngreso -> Ingreso[{Name}]", respuestaDAC);
            try
            {
                //TODO IMPLPEMENTAR
                _bEnLazoPresencia = true;
                //ActivarAntena(respuestaDAC, eCausaLecturaTag.eCausaLazoPresencia);
                //m_pInterfaz->SetEstadoBucle(m_bEnLazoPresencia);//TODO IMPLEMENTAR
                DecideCaptura(eCausaVideo.LazoPresenciaOcupado);
                GrabarVehiculos();
                LoguearColaVehiculos();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            _logger.Info("******************* LazoPresenciaIngreso -> Salida");
        }
        
        /// <summary>
        /// Mensaje que se activa al salir del bucle de salida.
        /// </summary>
        /// <param name="esModoForzado">Parametro que indica que es Forzado (1) o No (0)</param>
        /// <param name="RespDAC"></param>
        override public void LazoSalidaEgreso(bool esModoForzado, short RespDAC)
        {
            Stopwatch sw = new Stopwatch();

            sw.Restart();

            _logger.Info($"******************* LazoSalidaEgreso -> Ingreso RespDAC: [{RespDAC}]");
            LoguearColaVehiculos();
            bool bEnc = false;
            eVehiculo eIndex;

            try
            {
                if (_bEnLazoSal == false && esModoForzado == false)
                    LazoSalidaIngreso(true, 0); //Se fuerza el ingreso a lazo.

                _bEnLazoSal = false;

                // No está ni estuvo abierta en sentido opuesto
                //Solo para cerrada

                _logger.Info("m_bUltimoSentidoEsOpuesto[{Name}],IsSentidoOpuesto(false)[{Name}],IsSentidoOpuesto(true)[{Name}]", _bUltimoSentidoEsOpuesto ? "T" : "F", IsSentidoOpuesto(false) ? "T" : "F", IsSentidoOpuesto(true) ? "T" : "F");

                if (_logicaCobro.Estado != eEstadoVia.EVCerrada || (!_bUltimoSentidoEsOpuesto && !IsSentidoOpuesto(true)))
                {
                    if (ComitivaVehPendientes <= 1 && _logicaCobro.Estado == eEstadoVia.EVAbiertaPag)
                        _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;

                    if ((_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera) && _logicaCobro.ModoQuiebre != eQuiebre.EVQuiebreLiberado && (_logicaCobro.Estado != eEstadoVia.EVAbiertaPag))
                    {
                        if (ComitivaVehPendientes <= 1 && !_flagIniViol)
                        {
                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                            Vehiculo vehd = GetVehAnt();
                            //Aca hay que preguntar si esta abierta la bidireccional, si  
                            if (!EstaOcupadoBucleSalida())
                            {
                                //Cierro la barrera.

                                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                                _logger.Info("BARRERA ABAJO");
                            }

                            // Actualiza el estado de los mimicos en pantalla
                            Mimicos mimicos = new Mimicos();
                            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                        }
                        
                    }
                    else if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                    {
                        Vehiculo vehiculo = GetPrimeroSegundoVehiculo();
                        vehiculo.TipoViolacion = 'Q';
                        vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                        CategoriaPic(ref vehiculo, DateTime.Now, false, null);
                        vehiculo.Fecha = DateTime.Now;
                        ModuloEventos.Instance.SetViolacionXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, vehiculo);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();

                        ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VIA, listaDatosVia);
                        ClassUtiles.InsertarDatoVia(_logicaCobro.GetTurno, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Paso de Vehiculo en Quiebre");
                    }

                    GetVehTran().SalidaON = false;
                    GetVehTran().GetSalidaONClock();

                    DecideCaptura(eCausaVideo.LazoSalidaDesocupado);

                    if (ComitivaVehPendientes > 1)
                    {
                        GetVehIng().TipOp = GetVehAnt().TipOp;
                        GetVehIng().TipBo = GetVehAnt().TipBo;
                        GetVehIng().Operacion = GetVehAnt().Operacion;
                        GetVehIng().NumeroTransito = GetVehAnt().NumeroTransito + 1;
                        GetVehIng().Fecha = GetVehAnt().Fecha.AddSeconds(1);
                        GetVehIng().FormaPago = GetVehAnt().FormaPago;

                        eCausaVideo causaVideo = eCausaVideo.LazoSalidaDesocupado;
                        InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(), eCamara.Lateral, eTipoMedio.Foto, causaVideo, false);
                        GetVehAnt().ListaInfoFoto.Add(oFoto);
                        ModuloFoto.Instance.SacarFoto(GetVehAnt(), causaVideo, false, oFoto);

                    }

                    //Si el separador está libre y en ANT hay un vehículo genero el evento
                    if (!_enSeparSal && GetVehAnt().Init)
                    {
                        GeneraEventosTransito();
                    }

                    if (ComitivaVehPendientes > 0)
                    {
                        ComitivaVehPendientes--;
                        AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                        if(ComitivaVehPendientes >= 1)
                            AdelantarVehiculo(eMovimiento.eOpPago);
                    }                    

                    //TODO: ver condiciones en las cuales no se deberia limpiar la pantalla
                    if(ComitivaVehPendientes>0)
                    {
                        _flagIniViol = false;
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Comitiva, pendientes:" + ComitivaVehPendientes.ToString());
                    }
                    else if (_flagIniViol)
                    {
                        _flagIniViol = false;
                        
                        decimal vuelto = 0;
                        int monto = 0;
                        Vehiculo vehiculo = GetVehIng();
                        GetPrimeroSegundoVehiculo().CobroEnCurso = false;
                        GetPrimerVehiculo().CobroEnCurso = false;
                        List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vuelto, ref listaDatosVia3);
                        ClassUtiles.InsertarDatoVia(monto, ref listaDatosVia3);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia3);

                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);

                        Vehiculo vehAux = GetVehTran();
                        if (!vehAux.EstaPagado)
                            vehAux = GetVehAnt();

                        eCausaVideo causaVideo = eCausaVideo.LazoSalidaDesocupado;
                        InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(), eCamara.Lateral, eTipoMedio.Foto, causaVideo, false);
                        vehAux.ListaInfoFoto.Add(oFoto);
                        ModuloFoto.Instance.SacarFoto(vehAux, causaVideo, false, oFoto);
                    }
                    else if (GetPrimeroSegundoVehiculo().Categoria != 0 || GetPrimeroSegundoVehiculo().EnOperacionManual)
                    {
                        //AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                        Vehiculo vehiculo = GetVehIng();
                        GetPrimeroSegundoVehiculo().CobroEnCurso = false;
                        GetPrimerVehiculo().CobroEnCurso = false;                                         
                    }
                    else
                    {
                        // Se limpia la pantalla luego de la salida del vehiculo
                        ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);
                        //AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                        decimal vuelto = 0;
                        int monto = 0;
                        Vehiculo vehiculo = GetVehIng();
                        GetPrimeroSegundoVehiculo().CobroEnCurso = false;
                        GetPrimerVehiculo().CobroEnCurso = false;
                        List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vuelto, ref listaDatosVia3);
                        ClassUtiles.InsertarDatoVia(monto, ref listaDatosVia3);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia3);

                        ModuloPantalla.Instance.LimpiarTodo(true);
                        ModuloPantalla.Instance.LimpiarMensajes();
                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);

                        Vehiculo vehAux = GetVehTran();
                        if (!vehAux.EstaPagado)
                            vehAux = GetVehAnt();

                        eCausaVideo causaVideo = eCausaVideo.LazoSalidaDesocupado;
                        InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(), eCamara.Lateral, eTipoMedio.Foto, causaVideo, false);
                        vehAux.ListaInfoFoto.Add(oFoto);
                        ModuloFoto.Instance.SacarFoto(vehAux, causaVideo, false, oFoto);
                    }
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            DetenerTimerLazoSalida();
            LoguearColaVehiculos();
            sw.Stop();
            _loggerTiempos.Info(sw.ElapsedMilliseconds);
            _logger.Info("******************* LazoSalidaEgreso -> Salida");
        }


        /// <summary>
        /// Revisa si es necesario iniciar o detener un video para 
        /// </summary>
        /// <param name="oVehiculo"></param>
        override public void CapturaVideo(ref Vehiculo oVehiculo, ref eCausaVideo sensor, bool esManual = false)
        {
            if (oVehiculo != null)
            {
                // Inicio video
                bool bFoundAcc = false, iniciarVideo = false, finVideo = false, iniciarVideo2 = false, finVideo2 = false, iniciarVideo4 = false, finVideo4 = false;

                VideoAcc oVideoAcc = ModuloBaseDatos.Instance.ObtenerVideoAcc((int)sensor);
                if (oVideoAcc != null)
                    bFoundAcc = true;

                if (bFoundAcc || esManual)
                {
                    // En el caso de PANAVIAL, se filma video con ambas camaras
                    iniciarVideo = oVideoAcc.ComienzaGrabacion == "S"; // Chequear si este es para video siempre
                    if (iniciarVideo)
                    {
                        if (!oVehiculo.ListaInfoVideo.Exists(item => item.Camara == eCamara.Lateral))
                        {
                            InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(), eCamara.Lateral, eTipoMedio.Video, sensor);

                            oVehiculo.ListaInfoVideo.Add(oVideo);
                            ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                        }
                    }

                    finVideo = oVideoAcc.TerminaGrabacion == "S"; // Chequear si este es para video siempre
                    if (finVideo)
                    {
                        ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Lateral);
                    }

                    iniciarVideo2 = oVideoAcc.ComienzaGrabacion2 == "S"; // Chequear si este es para video siempre
                    if( iniciarVideo2 )
                    {
                        if (!oVehiculo.ListaInfoVideo.Exists(item => item.Camara == eCamara.Frontal))
                        {
                            InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(), eCamara.Frontal, eTipoMedio.Video, sensor);

                            oVehiculo.ListaInfoVideo.Add(oVideo);
                            ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                        }
                    }

                    finVideo2 = oVideoAcc.TerminaGrabacion2 == "S"; // Chequear si este es para video siempre
                    if( finVideo2 )
                    {
                        ModuloVideo.Instance.DetenerVideo( oVehiculo, sensor, eCamara.Frontal );
                    }

                    iniciarVideo4 = oVideoAcc.ComienzaGrabacion4 == "S"; // Chequear si este es para video siempre
                    if (iniciarVideo4 || esManual)
                    {
                        if (!oVehiculo.ListaInfoVideo.Exists(item => item.Camara == eCamara.Interna))
                        {
                            InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(), eCamara.Interna, eTipoMedio.Video, sensor);

                            oVehiculo.ListaInfoVideo.Add(oVideo);
                            ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                        }
                    }

                    finVideo4 = oVideoAcc.TerminaGrabacion4 == "S"; // Chequear si este es para video siempre
                    if (finVideo4)
                    {
                        ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Interna);
                    }
                }
                else
                    _logger.Trace("No se encontro la accion en la base de datos ");
            }
        }

        /// <summary>
        /// Revisa si es necesario iniciar o detener un video para 
        /// </summary>
        /// <param name="oVehiculo"></param>
        public override void CapturaFoto(ref Vehiculo oVehiculo, ref eCausaVideo sensor, bool esManual = false)
        {
            try
            {
                bool bFoundAcc = false, sacarFoto = false;
                VideoAcc oVideoAcc = ModuloBaseDatos.Instance.ObtenerVideoAcc((int)sensor);

                bFoundAcc = (oVideoAcc != null);

                if (bFoundAcc)
                {
                    sacarFoto = oVideoAcc.ComienzaGrabacion2 == "S"; // Chequear si este es para foto siempre

                    if (sensor == eCausaVideo.Manual || sacarFoto)
                    {
                        InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(), eCamara.Frontal, eTipoMedio.Foto, sensor, esManual);

                        // Si la foto no es de tipo manual, se agrega a la lista de medios del vehiculo
                        if (!esManual)
                            oVehiculo.ListaInfoFoto.Add(oFoto);
                        else
                        {
                            // Chequea si la lista ya contiene una foto de tipo manual
                            bool containsItem = oVehiculo.ListaInfoFoto.Any(item => item.EsManual == true);

                            // Si contiene una foto manual, la reemplaza. Si no, agrega la foto manual a la lista
                            if (containsItem)
                                oVehiculo.ListaInfoFoto[oVehiculo.ListaInfoFoto.FindIndex(ind => ind.EsManual == true)] = oFoto;
                            else
                                oVehiculo.ListaInfoFoto.Add(oFoto);
                        }

                        _logger.Trace("LogicaViaManual::CapturaFoto -> Saco foto");

                        ModuloFoto.Instance.SacarFoto(oVehiculo, sensor, esManual, oFoto);                        
                    }
                    else
                    {
                        _logger.Trace("LogicaViaManual::CapturaFoto -> No se saca foto por config de base");
                    }
                }
                else
                    _logger.Trace("No se encontro la accion en la base de datos ");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        /// <summary>
        /// Obtiene nombre de archivo de foto
        /// </summary>
        /// <returns></returns>
        private string ObtenerNombreFoto()
        {
            ConfigVia configVia = ModuloBaseDatos.Instance.ConfigVia;
            return $"{configVia.NumeroDeEstacion.ToString().PadLeft(2, '0')}{configVia.NumeroDeVia.ToString().PadLeft(3, '0')}{DateTime.Now.ToString("yyyyMMddHHmmssffffff")}.jpg";
        }

        /// <summary>
        /// Genera nombre de archivo de video
        /// </summary>
        /// <returns></returns>
        private string ObtenerNombreVideo()
        {
            ConfigVia configVia = ModuloBaseDatos.Instance.ConfigVia;
            return $"{configVia.NumeroDeEstacion.ToString().PadLeft(2, '0')}{configVia.NumeroDeVia.ToString().PadLeft(3, '0')}{DateTime.Now.ToString("yyyyMMddHHmmssffffff")}.mp4";
        }

        public override eErrorTag ValidarTagBaseDatos(ref InfoTag oInfoTag, ref Vehiculo oVehiculo, bool bUsarDatosVehiculo, ref TagBD tagBD)
        {
            _logger.Debug("ValidarTagBaseDatos -> Inicio");
            eErrorTag ret = eErrorTag.Error;
            EnmStatusBD estado = EnmStatusBD.OK;
            try
            {
                // Si se encontró el numero de tag
                if (tagBD != null && oInfoTag != null && !string.IsNullOrEmpty(tagBD.NumeroTag))
                {
                    _logger.Info("ValidarTagBaseDatos -> Tag[{0}]", tagBD.NumeroTag);
                    //InfoTag infoTag = new InfoTag();

                    //Datos del Vehiculo
                    oInfoTag.Patente = tagBD.Patente;
                    oInfoTag.NumeroTag = tagBD.NumeroTag;
                    oInfoTag.Categoria = tagBD.Categoria;

                    oInfoTag.Ruc = tagBD.DocumentoCliente;
                    oInfoTag.Subfp = tagBD.Agrupacion;

                    oInfoTag.GiroRojo = tagBD.MaximoGiroRojo;
                    oInfoTag.SaldoInicial = tagBD.SaldoVerdadero;
                    oInfoTag.SaldoFinal = tagBD.SaldoVerdadero;
                    oInfoTag.SaldoPreRecarga = tagBD.SaldoVerdadero;

                    oInfoTag.HabilitadoEstacion = true; //TODO: Traer de BD, cual tabla?

                    //Caracteristicas del Vehiculo
                    oInfoTag.Modelo = tagBD.Modelo;
                    oInfoTag.Marca = tagBD.Marca;
                    oInfoTag.Color = tagBD.Color;

                    //Datos del usuario
                    oInfoTag.NombreCuenta = tagBD.NombreCliente;
                    oInfoTag.Cuenta = tagBD.NumeroCuenta;
                    oInfoTag.NroCliente = tagBD.Numerocliente.GetValueOrDefault();
                    oInfoTag.TipoDocumento = tagBD.TipoDocumento;

                    // Datos del vehiculo
                    oInfoTag.TipoTag = (eTipoCuentaTag)tagBD.TipoCuenta.GetValueOrDefault();

                    //7 solo Pago en Via, 9 Pago en Vioa o Prepago
                    oInfoTag.PagoEnVia = tagBD.Estado == 7 || tagBD.Estado == 9 || tagBD.CodigoAccion == 7 || tagBD.CodigoAccion == 9 ? 'S' : 'N';
                    oInfoTag.PagoEnViaPrepago = tagBD.Estado == 9 || tagBD.CodigoAccion == 9 ? true : false;
                    oInfoTag.AfectaDetraccion = tagBD.AfectaDetraccion;

                    oInfoTag.TipBo = tagBD.TipoBoleto.GetValueOrDefault();
                    oInfoTag.TipoTarifa = tagBD.TipoTarifa.GetValueOrDefault();
                    oInfoTag.ConSaldo = tagBD.ConSaldo;
                    oInfoTag.TipoSaldo = tagBD.TipoSaldo;

                    //Mensaje a mostrar
                    oInfoTag.Mensaje = tagBD.MensajePantalla;
                    oInfoTag.MensajeAuxiliar = tagBD.MensajePantallaAuxiliar;
                    oInfoTag.MensajeDisplay = tagBD.MensajeDisplay;
                    oInfoTag.MensajeDisplayAuxiliar = tagBD.MensajeDisplayAuxiliar;

                    if (tagBD.Estado == 103)
                        return eErrorTag.Vencido;

                    //Estado del Tag
                    if (tagBD.Habilitado != 'S')
                        return eErrorTag.NoHabilitado;

                    oInfoTag.Confirmado = true;

                    if (bUsarDatosVehiculo)
                    {
                        oInfoTag.CategoTabulada = oVehiculo.Categoria;
                    }
                    else
                    {
                        oInfoTag.CategoTabulada = 1;
                    }
                    
                    //Chip y categoria
                    //si hubo tabulacion
                    if (!_logicaCobro.ModoPermite(ePermisosModos.TSCsinTabulacion))
                    {
                        //si controla categoria
                        if (oInfoTag.TipOp == 'C' && tagBD.ControlaCategoria == 'S')
                        {
                            if (oInfoTag.CategoTabulada != oInfoTag.Categoria)
                            {
                                oInfoTag.MensajeAuxiliar = "Categoria distinta";
                                return eErrorTag.CategoriaDistinta;
                            }
                        }
                    }

                    if (oInfoTag.Ruc == "")
                    {
                        oInfoTag.MensajeAuxiliar = "Sin cliente asociado";
                        return eErrorTag.SinCliente;
                    }

                    //Vencimiento?

                    //Validar Forma de Pago
                    CategoFPagoABuscar oCategoFPagoABuscar = new CategoFPagoABuscar();
                    oCategoFPagoABuscar.Categoria = oInfoTag.Categoria;
                    oCategoFPagoABuscar.TipoOperacion = oInfoTag.TipOp;
                    oCategoFPagoABuscar.TipoFPago = oInfoTag.TipBo;

                    List<CategoFPago> oListaCategoFPago = ModuloBaseDatos.Instance.BuscarCategoFPago(oCategoFPagoABuscar, ref estado);

                    if (estado == EnmStatusBD.SINRESULTADO)
                    {
                        oInfoTag.MensajeAuxiliar = "Categoria no habilitada";
                        return eErrorTag.CategoriaNoHabilitada;
                    }

                    // Se busca la tarifa
                    short shCategoria = oInfoTag.Categoria;

                    TarifaABuscar oTarifaABuscar = new TarifaABuscar();

                    oTarifaABuscar.Catego = shCategoria;
                    oTarifaABuscar.Estacion = ModuloBaseDatos.Instance.ConfigVia.NumeroDeEstacion;
                    oTarifaABuscar.TipoTarifa = oInfoTag.TipoTarifa;
                    oTarifaABuscar.FechAct = DateTime.Now;
                    oTarifaABuscar.FechaComparacion = DateTime.Now;
                    oTarifaABuscar.FechVal = DateTime.Now;
                    oTarifaABuscar.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido;
                    Tarifa tarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar); 

                    if (tarifa.EstadoConsulta != EnmStatusBD.OK)
                    {
                        //se intenta volver a buscar
                        tarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar);
                        if (tarifa.EstadoConsulta != EnmStatusBD.OK)
                        {
                            _logger.Debug("ValidarTagBaseDatos -> Error en BuscarTarifa");
                            oInfoTag.MensajeAuxiliar = "Tarifa Inexistente";
                            return eErrorTag.TarifaInexistente;
                        }
                    }

                    oInfoTag.TipDH = tarifa.CodigoHorario;
                    oInfoTag.Tarifa = tarifa.Valor;
                    oInfoTag.CategoDescripcionLarga = tarifa.Descripcion;
                    if (oInfoTag.PagoEnViaPrepago)
                        oVehiculo.CategoriaDesc = tarifa.Descripcion;

                    _logger.Debug("ValidarTagBaseDatos -> Tarifa [{0}] - TipDH [{1}] - DescCat [{2}]", oInfoTag.Tarifa, oInfoTag.TipDH, oInfoTag.CategoDescripcionLarga);

                    byte btTitar = oInfoTag.TipoTarifa;
                    if (oInfoTag.PagoEnViaPrepago)
                    {
                        //se modifican valores par apermitir validación
                        tagBD.ConSaldo = 'S';
                        tagBD.TipoSaldo = 'M';
                        oInfoTag.TipoTarifa = 0;
                    }

                    if (tagBD.ConSaldo == 'S')//Si usa saldo
                    {
                        if (tagBD.TipoSaldo == 'M') //Monto
                        {
                            if (oInfoTag.PagoEnViaPrepago)
                                //si es pago en via y tiene saldo se cambia el tipBo para enviar el cobro
                                oInfoTag.TipBo = 'P';

                            if (oInfoTag.TipoTarifa > 0 && oInfoTag.Tarifa > oInfoTag.SaldoInicial)
                                return eErrorTag.SinSaldo;

                            //oInfoTag.Tarifa = decimal.ToUInt64(tarifa.Valor);

                            if (oVehiculo.TipoRecarga > 0 //Recarga reciente, no se considera saldo
                                || (oInfoTag.SaldoInicial >= oInfoTag.Tarifa)
                                || (oInfoTag.SaldoInicial - oInfoTag.Tarifa >= (oInfoTag.GiroRojo * -1)))
                            {
                                ret = eErrorTag.NoError;
                            }
                            else
                            {
                                ret = eErrorTag.SinSaldo;
                            }

                            //regresamos el valor de TipoTarifa
                            if (oInfoTag.PagoEnViaPrepago)
                                oInfoTag.TipoTarifa = btTitar;
                        }
                        else if (tagBD.TipoSaldo == 'V')//Viajes
                        {
                            if (oInfoTag.SaldoInicial > 0)
                            {
                                ret = eErrorTag.NoError;
                            }
                            else
                            {
                                ret = eErrorTag.SinViajes;
                            }
                        }
                    }
                    else //Sino tiene saldo y llego hasta acá, asumo SINERROR
                    {
                        ret = eErrorTag.NoError;
                    }
                    //tagBD.TieneAbono
                }
                else
                {
                    ret = eErrorTag.Desconocido;
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
                ret = eErrorTag.Desconocido;
            }
            _logger.Debug("ValidarTagBaseDatos -> Fin");
            return ret;
        }

        /// <summary>
        /// Asigna el valor de la Tarjeta.
        /// </summary>
        async private void GeneraEventosTransito()
        {
            _logger.Info("Genero EventoTransito");

            //Si no tiene asignado tipo transito
            if (GetVehAnt().Operacion == "" )
            {
                if (GetVehAnt().EstaPagado || ComitivaVehPendientes > 0)
                    GetVehAnt().Operacion = "TR";
                else if (GetVehAnt().Init)
                {
                    GetVehAnt().Operacion = "VI";
                }
                else
                {
                    return;
                }
            }

            ConfigVia oConfigVia = ModuloBaseDatos.Instance.ConfigVia;

            if (GetVehAnt().Operacion == "VI")
            {
                    _loggerTransitos?.Info($"V;{DateTime.Now.ToString("HH:mm:ss.ff")};{GetVehAnt().Categoria};{GetVehAnt().TipOp};{GetVehAnt().TipBo};{GetVehAnt().GetSubFormaPago()};{GetVehAnt().InfoDac.Categoria};{GetVehAnt().NumeroTransito}");
                    ModuloEventos.Instance.SetViolacionXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, GetVehAnt());

            }
                
            else
            {
                _loggerTransitos?.Info($"T;{DateTime.Now.ToString("HH:mm:ss.ff")};{GetVehAnt().Categoria};{GetVehAnt().TipOp};{GetVehAnt().TipBo};{GetVehAnt().GetSubFormaPago()};{GetVehAnt().InfoDac.Categoria};{GetVehAnt().NumeroTransito}");

                ModuloEventos.Instance.SetPasadaXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, GetVehAnt(), ComitivaVehPendientes);
                ModuloOCR.Instance.TransitoOCR(GetVehAnt());
            }

            _logicaCobro.FecUltimoTransito = DateTime.Now;

            //Limpio el vehículo ANT
            _logger.Info("GenerarEventoTransito -> Limpio VehAnt");
            _vVehiculo[(int)eVehiculo.eVehAnt] = new Vehiculo();
        }
        private bool IsSentidoOpuesto(bool bEsCritico)
        {
            return bEsCritico ? DAC_PlacaIO.Instance.EntradaBidi() : _sentidoOpuesto;
        }



        private void OnViolacionViaApagada()
        {


            List<RegistroDacMD> oListaDac = new List<RegistroDacMD>();
            RegistroDacMD oRegDac = null;
            ulong ulNumTurn = 0;



            //TODO!!!
            //El DAC en Modo D no está salvando las violaciones

            _logger.Info("*****OnViolacionViaApagada -> Inicio");

            try
            {

                ViolacionApagada violacion = new ViolacionApagada();

                DAC_PlacaIO.Instance.ViolacionViaApagada(ref oListaDac, ref violacion/*reint, ref overr, ref finok, ref cntmd*/);

                violacion.SentidoOpuesto = _ultimoSentidoEsOpuesto;
                violacion.ViolacionesReg = oListaDac.Count;


                _logger.Info("OnViolacionApagada Transitos recibidos [%d]", oListaDac.Count);


                if (oListaDac.Count > 0)
                {
                    //Manda un único evento con el resumen de violaciones con vía cerrada		
                    ulNumTurn = _logicaCobro.GetTurno.NumeroTurno;
                    if (_logicaCobro.Estado == eEstadoVia.EVCerrada)
                        ulNumTurn++;

                    Turno oTurno = new Turno();



                    ModuloEventos.Instance.SetViolacionesApagadas(ModuloBaseDatos.Instance.ConfigVia, oTurno, violacion);


                    //Solo generamos el detalle si la vía no estaba en sentido opuesto
                    if (!_ultimoSentidoEsOpuesto)
                    {

                        int i = 0;
                        while (i < oListaDac.Count)
                        {
                            oRegDac = oListaDac[i];
                            //Funcion q pasándole el registro recibido del DAC manda una violacion
                            RegistroViolacionViaApagada(ref oRegDac, ref oTurno);
                            i++;
                        }
                    }
                    else
                    {
                        _logger.Info("OnViolacionApagada -> Vía en Sentido Opuesto, no generamos violaciones");

                    }
                }
                else
                {
                    _logger.Info("OnViolacionApagada -> La Lista llegó vacía");
                }


            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logger.Info("*****OnViolacionApagada -> Fin");


        }

        private void RegistroViolacionViaApagada(ref RegistroDacMD oRegDac, ref Turno oTurno)
        {
            _logger.Info(oRegDac);
            Vehiculo oVehMD = null;


            _logger.Info("*****RegistroViolacionViaApagada -> Inicio");


            try
            {
                if (oRegDac != null && oRegDac.fechahora > DateTime.MinValue)
                {
                    oVehMD = new Vehiculo();

                    oVehMD.Operacion = "VI";     //Tipo de Operación
                    oVehMD.TipoViolacion = 'R';  //Asignamos el tipo de violación

                    //Incremento el número de transitos
                    oVehMD.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                    //m_pInterfaz->SetUltimoticket(pVehMD->GetNumTr(), 0);



                    //Modo Barrera Vía Cerrrada 'A'
                    oVehMD.ModoBarrera = 'A';



                    oVehMD.Fecha = oRegDac.fechahora;

                    CategoriaPic(ref oVehMD, oRegDac.fechahora, true, oRegDac); //Asignamos la categoria, fecha, peanas,...

                    ModuloEventos.Instance.SetViolacionXML(ModuloBaseDatos.Instance.ConfigVia, oTurno, oVehMD);

                }

            }

            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logger.Info("*****RegistroViolacionViaApagada -> Fin");

        }



        public override void LazoSalidaIngreso(bool esModoForzado, short RespDAC)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            _logger.Info($"******************* LazoSalidaIngreso -> Ingreso RespDAC [{RespDAC}]");
            IniciarTimerLazoSalida();

            try
            {
                if (_bEnLazoSal == true && esModoForzado == false)
                    LazoSalidaEgreso(true, 0);

                _bEnLazoSal = true;
                GetVehTran().SalidaON = true;
                GetVehTran().SetSalidaONClock();

                DecideCaptura(eCausaVideo.LazoSalidaOcupado);
                if (ComitivaVehPendientes <= 1 && _logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera)
                {
                    ModuloDisplay.Instance.Enviar(eDisplay.BNV);
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                }
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Se limpia la pantalla luego de la salida del vehiculo
                ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.OcuparLazo);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "LazoSalidaIngreso");
            }
            sw.Stop();
            _loggerTiempos.Info(sw.ElapsedMilliseconds);
            _logger.Info("******************* LazoSalidaIngreso -> Salida");
        }
        /// <summary>
        /// Selecciona el vehiculo correspondiente al sensor indicado
        /// Y llama a la captura de video y foto, para revisar la configuración necesaria
        /// </summary>
        /// <param name="sensor"></param>
        public override void DecideCaptura(eCausaVideo sensor, ulong NumeroVehiculo = 0)
        {
            {
                Vehiculo oVehiculo = null;

                _logger.Trace("LogicaViaDinamica::DecideCaptura -> sensor [{name}]", sensor.ToString());

                if (NumeroVehiculo == 0)
                {
                    oVehiculo = ObtenerVehiculoSegunSensor(sensor);
                }
                else
                {
                    oVehiculo = GetVehiculo(BuscarVehiculo(NumeroVehiculo, true));
                    //Esto por si no encontré el vehiculo
                    if (oVehiculo.NumeroVehiculo == 0)
                        oVehiculo = ObtenerVehiculoSegunSensor(sensor);
                }

                
                CapturaVideo(ref oVehiculo, ref sensor);
                CapturaFoto(ref oVehiculo, ref sensor);
                _logger.Info("DecideCaptura -> Sensor [{name}] NumeroVehiculo [{name}] TipOp [{name}] TipBo [{name}] FP [{name}]", sensor.ToString(), oVehiculo?.NumeroVehiculo, oVehiculo?.TipOp, oVehiculo?.TipBo, oVehiculo?.FormaPago.ToString());
                _logger.Info("DecideCaptura -> Fin");
            }
        }

        private Vehiculo ObtenerVehiculoSegunSensor(eCausaVideo sensor)
        {
            Vehiculo oVehiculo = null;

            if (sensor == eCausaVideo.LazoPresenciaOcupado)
            {
                oVehiculo = GetVehIng(true, true);
            }
            else if (sensor == eCausaVideo.Violacion)
            {
                oVehiculo = GetVehIng(true);
            }
            else if (sensor == eCausaVideo.LazoPresenciaDesocupado)
            {
                oVehiculo = GetVehIng(true);
            }
            else if (sensor == eCausaVideo.SeparadorSalidaOcupado)
            {
                oVehiculo = GetVehTran();
            }
            else if (sensor == eCausaVideo.SeparadorSalidaDesocupado)
            {
                if (_logicaCobro.Estado == eEstadoVia.EVAbiertaPag)
                    oVehiculo = GetVehTran();
                else
                    oVehiculo = GetVehIng();
            }
            else if (sensor == eCausaVideo.LazoSalidaOcupado)
            {
                if (_logicaCobro.Estado == eEstadoVia.EVAbiertaPag)
                    oVehiculo = GetVehTran();
                else
                    oVehiculo = GetVehIng();
            }
            else if (sensor == eCausaVideo.LazoSalidaDesocupado)
            {
                oVehiculo = GetVehVideo();
            }
            else if (sensor == eCausaVideo.Pagado || sensor == eCausaVideo.PagadoExento || sensor == eCausaVideo.PagadoTelepeaje)
            {
                oVehiculo = GetVehTran();
            }
            else if (sensor == eCausaVideo.Categorizar)
            {
                oVehiculo = GetVehIngCat();
            }
            else if (sensor == eCausaVideo.LazoIntermedioDesocupado)
            {
                oVehiculo = GetVehIng();
            }
            else if (sensor == eCausaVideo.LazoIntermedioOcupado)
            {
                oVehiculo = GetVehIng(true, true);
            }
            return oVehiculo;
        }

        private void LazoIngSemPaso(short sobligado, long lobligado)
        {

            //Si esta abierto el dialog para ingresar el numero de patente
            //para el ticket de clearing lo cierro
            _logger.Info("INGRESO A LazoIngSemPaso");

            //Lo agregue en OcultarDialogos, y lo llama en OnSeparSal


            //Si el vehiculo ingresado tiene tag, el semaforo debe quedar verde
            //tambien si hay vehiculos en la cola de cobro anticipado
            bool semaforoEnVerde = false;
            if (ComitivaVehPendientes > 0)
                semaforoEnVerde = true;
            if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera
                    && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
            {
                semaforoEnVerde = _logicaCobro.ModoPermite(ePermisosModos.SemaforoPasoVerdeEnQuiebre);
            }

            if (!semaforoEnVerde)
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

            _habiCatego = true;
            _flagCatego = false;

            //TODO IMPLEMENTAR


            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
            {
                Autotabular();

                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }

        }

        /// <summary>
        /// Establece segun la configuracion, la categoria correspondiente.
        /// </summary>
        /// <param name="bMostrarBNVModoD">muestra en mensaje de bienvenida. Cuando se abre un turno en modo D. o la vía arranca en modo D</param>
        private void Autotabular(bool bMostrarBNVModoD = false)
        {
            byte byMonocategoria = 0;

            _logger.Info("Ingresa a Autotabular()");

            //Si m_nModoQuiebre = EVQuiebreControlado o EVQuiebreLiberado
            //no  tabular

            //Mando al display el mensaje de BIENVENIDO//MAA 2019108


            //if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera)
            //{
            //    //En modo D también es autotabulante aunque no esté configurada
            //    //la vía como autotabulante
            //    if ((_bAutotabulante && m_nModoQuiebre != EVQuiebreControlado)
            //    )
            //    {
            //        ConfigV.GetMonoCatego(&byMonocategoria);
            //        CString csAux = "";
            //        CVehiculo* VehIng;
            //        VehIng = GetVehIngCat();
            //        m_ucCatego_Autotabular = byMonocategoria;
            //        csAux.Format("Autotabular, Categoria: %d", byMonocategoria);
            //        m_bTabulacionAutomatica = true;
            //        //Si es modo D y el vehículo no tiene forma de pago y está ocupado
            //        //llamo a OnCatego o no es modo D. Sino muestro el mensaje de Bienvenida
            //        if (m_sModo == "D" && VehIng->GetNoVacio()
            //           && !VehIng->GetFormaPago() || m_sModo != "D")
            //            OnCatego(byMonocategoria, CATEGO_NORMAL);
            //        else
            //        {
            //            if (bMostrarBNVModoD)
            //                Mensaje("BNV", DEF_MSG_VAR);
            //        }
            //        m_bTabulacionAutomatica = false;
            //    }
            //    else
            //    {
            //        //m_ucCatego_Autotabular=0;
            //        Mensaje("BNV", DEF_MSG_VAR);
            //        if (m_nModoQuiebre)
            //        {
            //            m_pInterfaz->SetCatego("0");
            //            m_pInterfaz->SetPrecio(0);
            //            m_pInterfaz->SetFormaPago(0);
            //        }
            //    }
            //}
            //else
            //{
            //    //Quiebre liberado
            //    Mensaje("PAG", DEF_MSG_VAR);
            //    Mensaje(DEF_MSG_CLEARDPY, DEF_MSG_DCH);
            //    m_pInterfaz->SetCatego("0");
            //    m_pInterfaz->SetPrecio(0);
            //    m_pInterfaz->SetFormaPago(0);
            //}
        }


        public override void SeparadorSalidaEgreso(bool esModoForzado, short RespDAC)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();

            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';
            _logger.Info("******************* SeparadorSalidaEgreso -> Ingreso[{Name}]", respuestaDAC);
            LoguearColaVehiculos();
            try
            {


                short catego = 0;
                bool bEnc, bTeniaFalsaSalida, bBarreraAbierta;
                eVehiculo eIndex;
                DateTime T1 = DateTime.Now;//m_Fecha;
                bool bSendLogica1 = false;
                bool bSendLogica2 = false;
                bool bSendLogica3 = false;
                byte btFiltrarSensores = 0;



                _enSeparSal = false;



                _logger.Info("SeparadorSalidaEgreso -> lRetDAC:[{Name}] cRetDAC:[{Name}]", RespDAC, respuestaDAC);


                /****** MANUAL ******/
                //Primero que nada tratamos de bajar la barrera

                //Si no está abierta en sentido opuesto
                //Si no existe el separador de salida
                //Si el siguiente no tiene tag
                //y el BPA está libre
                //NOTA: Sin Separador no detectamos Marcha Atras

                Vehiculo VehIng;
                VehIng = GetVehIng();

                // No está ni estuvo abierta en sentido opuesto
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(false))
                {
                    if (SingletonConfiguracionDAC.Instance.GetTerminalDAC(ConfiguracionDAC.EXISTE_SEP_SAL, ModosDAC.Manual).Valor == 0)
                    {

                        //No estamos en Quiebre Liberado
                        if ((_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera))
                        {
                            //Pongo el semáforo de paso en Rojo por las dudas
                            //Si el vehiculo ingresado no tiene tag
                            //Ni hay nadie en la cola de cobro anticipado
                            
                        }
                    }
                }

                /************************************************/
                //JAS
                //12/9/2007
                // Si no existe el separador no hay retroceso
                // Forzamos hacia adelante
                if (SingletonConfiguracionDAC.Instance.GetTerminalDAC(ConfiguracionDAC.EXISTE_SEP_SAL, ModosDAC.Manual).Valor == 0)
                    respuestaDAC = 'D';
                /************************************************/


                //Si se fue en medio de una venta
                //terminamos la venta		
                if (respuestaDAC == 'D' || respuestaDAC == 'T')
                {
                    /* //TODO IMPLEMENTAR
                     * if (_logicaCobro.Estado == eEstadoVia.EVAbiertaVenta)
                    {
                        //No subimos la barrera
                        m_pVenta->FinalVenta(false, true);

                        //m_FlagIniViol=true;
                    }*/

                    //desde aqui
                    if (/*!PagoSobreLazoHabilitado() ||*/ _logicaCobro.Estado != eEstadoVia.EVAbiertaPag)
                    {
                        /*
                        bool bTeniaConf = false;
                        if (m_pConfirmaMSG->GetConceptoActual() != CConfirmacion::T_SinConcepto)
                            bTeniaConf = true;


                        OcultarDialogos();


                        //Limpiamos el estado de confirmacion				
                        if (m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_LecturaManualTag ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_OpCerrada ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_OpAbortada ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_CobranzaConViaEnQ_TPago ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_CobranzaConViaEnQ_TBoleto ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_ComprobanteManualPagDif ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_RetencionPASE ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_RetiroParcial ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_FondoCaja ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_Parte ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_ViajeReciente ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_CierreBarreraManual ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_FranquiciaSupervision ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_FotoClearing ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_CancelacionVenta ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_PagoDiferidoFinal ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_RenovacionAbono ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_LecturaTag ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_Foto ||
                            m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_FotoClearingEmision
                            )
                        {
                            m_pConfirmaMSG->Limpiar(true); //Limpio los estados y la pantalla.
                        }
                        //JMRS MONEDA
                        //Si la via no queda en estado de confirmacion
                        //limpiamos la pantalla por las dudas
                        if (m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_SinConcepto && bTeniaConf)
                        {
                            Mensaje("", DEF_MSG_L01);
                            Mensaje("", DEF_MSG_L02);
                            Mensaje("", DEF_MSG_L03);
                        }


                        */
                        //Pongo el semáforo de paso en Rojo por las dudas
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                    }
                    //hasta aqui




                }

                switch (_logicaCobro.Estado)
                {
                    case eEstadoVia.EVCerrada:
                        // No está ni estuvo abierta en sentido opuesto
                        if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                        {


                            DecideCaptura(eCausaVideo.SeparadorSalidaDesocupado);



                            if (respuestaDAC == 'M')
                            {

                                Vehiculo oVehiculoA = GetVehIng();
                                CategoriaPic(ref oVehiculoA, T1, false, null);//Recibe la categorizacion que hizo el PIC
                                                                              //no generamos fallas porque al retroceder puede ser que no pise sensores


                                // TODO ver si tiene la tarifa

                                // Se envia mensaje a modulo de video continuo
                                ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Detectado, null, null, VehIng );
                                
                                //Determino la categoría del tránsito. Si el DAC devuelve < 0 dejo 0, sino
                                //el valor devuelto
                                if (oVehiculoA.InfoDac.Categoria < 0)
                                    catego = 0;
                                else
                                    catego = oVehiculoA.InfoDac.Categoria;

                                //Si no estamos en modo autista
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                {
                                    //Genero un evento de marcha atrás
                                    EnviarEventoMarchaAtras(ref oVehiculoA, T1, catego, eTipoRetroceso.MarchaAtras_M);
                                }

                                _logger.Info("OnSeparSal -> Via Cerrada, Modo <> D, marcha atrás (M)");
                            }
                            else
                            {
                                //Adelantamos el vehiculo para que este en VehTran
                                AdelantarVehiculo(eMovimiento.eOpPago);
                                Vehiculo oVehiculoA = GetVehTran();
                                //Violación
                                if (Violacion(ref oVehiculoA))
                                    AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                            }
                        }
                        break;

                    case eEstadoVia.EVAbiertaLibre:
                        goto case eEstadoVia.EVAbiertaCat;
                    case eEstadoVia.EVAbiertaCat:
                        //Si hay retroceso sin salida M vuelvo a categorizar
                        //con la categoría del vehículo ing
                        if (respuestaDAC == 'M' && GetVehIng().Categoria > 0)
                        {
                            _flagIniViol = false;

                            //OnCatego(GetVehIng().Categoria, CATEGO_RECATEGORIZAR); //TODO IMPLEMENTAR Como haga para categorizar?
                        }
                        goto case eEstadoVia.EVQuiebreBarrera;
                    case eEstadoVia.EVQuiebreBarrera:
                    case eEstadoVia.EVModoSup:

                        DecideCaptura(eCausaVideo.SeparadorSalidaDesocupado);
                        //segun la accion y la categoria del vehiculo	
                        switch (respuestaDAC)
                        {
                            case 'T':   //Marcha atrás luego de liberar el separador
                                        //Si el vehículo ANT tiene datos lo paso a TRAN
                                if (GetVehAnt().Init)
                                {
                                    //Genero un evento de marcha atrás con TRA porque 
                                    //al llamar a RetrocederVehiculo para de ANT a TRA
                                    eIndex = eVehiculo.eVehTra;

                                    //Saco el flag de inicio de violación
                                    _flagIniViol = false;
                                    _logger.Info("Retroceso de vehiculo");
                                    GetVehAnt().Reversa = true;
                                    RetrocederVehiculo();
                                }
                                else
                                {
                                    if (GetVehTran().Init)
                                    {
                                        eIndex = eVehiculo.eVehTra;
                                        Vehiculo oVehiculoB = GetVehTran();
                                        //Consulto al PIC para saber lo que detecto
                                        CategoriaPic(ref oVehiculoB, T1, false, null);

                                        // TODO ver si tiene la tarifa
                                        // Se envia mensaje a modulo de video continuo
                                        ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Detectado, null, null, GetVehTran() );
                                    }
                                    else
                                    {
                                        eIndex = eVehiculo.eVehIng;

                                        if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.Nada)
                                        {
                                            //Limpiamos el estado y la pantalla porque no lo hizo ingreso
                                            _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;


                                            //TODO IMPLEMENTAR Limpiar Interfaz
                                            /*m_pInterfaz->SetCatego("0");
                                            m_pInterfaz->SetPrecio(0);
                                            m_pInterfaz->SetFormaPago(0);*/
                                        }
                                        if (ComitivaVehPendientes > 0)
                                            _logicaCobro.Estado = eEstadoVia.EVAbiertaPag;
                                        //Es una violación
                                        //No consulto el PIC porque se hace dentro de Violacion
                                        Vehiculo oVehiculoB = GetVehIng();
                                        Violacion(ref oVehiculoB);
                                        EnviarEventoVehAnt();
                                        //Asigno ING a ANT

                                        _vVehiculo[(int)eVehiculo.eVehAnt] = GetVehIng();
                                        //Limpiamos VehIng
                                        _vVehiculo[(int)eVehiculo.eVehIng] = new Vehiculo();
                                        //VehIng->Clear(false);
                                        //Le asigno un numero
                                        AsignarNumeroVehiculo(eVehiculo.eVehIng);

                                        EnviarEventoVehAnt();

                                        //Mensaje(DEF_MSG_CLEARDPY, DEF_MSG_DCH);

                                        if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                                            Autotabular();
                                    }
                                }

                                //Determino la categoria, si hay categoría ingresada por el operador
                                //conservo esa, sino si el DAC es > 0 uso el DAC y sino es 0
                                catego = 0;
                                if (GetVehiculo(eIndex).Categoria > 0)
                                    catego = GetVehiculo(eIndex).Categoria;
                                else
                                    if (GetVehiculo(eIndex).InfoDac.Categoria > 0)
                                    catego = GetVehiculo(eIndex).InfoDac.Categoria;

                                //Si no estamos en modo autista
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                {
                                    Vehiculo oVehiculoB = GetVehiculo(eIndex);
                                    //Genero un evento de marcha atrás con el vehículo de eIndex
                                    EnviarEventoMarchaAtras(ref oVehiculoB, T1, catego, eTipoRetroceso.MarchaAtras_T);
                                }

                                // TODO ver si tiene la tarifa
                                // Se envia mensaje a modulo de video continuo
                                ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Detectado, null, null, GetVehiculo( eIndex ) );

                                break;

                            case 'M':   //Marcha atrás sin liberar separador					
                                _flagIniViol = false;
                                //Si el vehículo Tran tiene datos genero el evento de marcha atrás
                                //con ese vehículo, sino con ing
                                eIndex = eVehiculo.eVehIng;
                                if (GetVehTran().Init)
                                    eIndex = eVehiculo.eVehTra;

                                Vehiculo oVehiculo = GetVehiculo(eIndex);
                                //Consulto al PIC para saber lo que detecto
                                CategoriaPic(ref oVehiculo, T1, false, null);

                                // TODO ver si tiene la tarifa
                                // Se envia mensaje a modulo de video continuo
                                ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Detectado, null, null, GetVehiculo( eIndex ) );

                                //Determino la categoria, si hay categoría ingresada por el operador
                                //conservo esa, sino si el DAC es > 0 uso el DAC y sino es 0
                                catego = 0;
                                if (GetVehiculo(eIndex).Categoria > 0)
                                    catego = GetVehiculo(eIndex).Categoria;
                                else
                                    if (GetVehiculo(eIndex).InfoDac.Categoria > 0)
                                    catego = GetVehiculo(eIndex).InfoDac.Categoria;

                                //Si no estamos en modo autista
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                {
                                    Vehiculo oVehiculoB = GetVehiculo(eIndex);
                                    //Genero un evento de marcha atrás con el vehículo de eIndex
                                    EnviarEventoMarchaAtras(ref oVehiculoB, T1, catego, eTipoRetroceso.MarchaAtras_M);
                                }
                                break;

                            case 'D':   //Adelante
                                if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.Nada)
                                {
                                    //Limpiamos el estado y la pantalla porque no lo hizo ingreso
                                    _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;

                                    if (ComitivaVehPendientes > 0)
                                        _logicaCobro.Estado = eEstadoVia.EVAbiertaPag;
                                    //TODO IMPLEMENTAR Limpiar Interfaz
                                    //m_pInterfaz->SetCatego("0");
                                    //m_pInterfaz->SetPrecio(0);
                                    //m_pInterfaz->SetFormaPago(0);
                                }


                                //Adelantamos el vehiculo para que este en VehTran
                                AdelantarVehiculo(eMovimiento.eOpPago);
                                Vehiculo oVehiculoA = GetVehTran();
                                if (Violacion(ref oVehiculoA))
                                {
                                    //Limpiamos VehIng para mostrar la cola limpia
                                    //GetVehIng()->Clear(false);
                                    _vVehiculo[(int)eVehiculo.eVehIng] = new Vehiculo();

                                    if (GetVehIng().NumeroVehiculo == 0) //Para evitar un vehing sin numero
                                        AsignarNumeroVehiculo(eVehiculo.eVehIng);

                                    AdelantarVehiculo(eMovimiento.eSalidaSeparador);


                                    //TODO IMPLEMENTAR Via Manual con antena
                                    //if (_infoTagEnCola.GetNumeroTag() != "")
                                    //{
                                    //    //Lo pasamos a VehIng
                                    //    //TODO si hay 3 vehiculos?
                                    //    AsignarTagEnCola(true);
                                    //}
                                    //	asignartag
                                    //if (GetVehIng()->GetRefInfoTag()->GetTagOK())
                                    //    TagIpicoVerificarManual();


                                    //Si sale un veh insertado en violacion y la cola no esta vacía
                                    //recupero el primer veh de la cola
                                    //else if (m_ColaVehPagoAd.GetCount() > 0)
                                    //{
                                    //    //Pasamos el primer vehiculo a vehtran
                                    //    TransferirVehColaPagoAd(GetVehTran());
                                    //    //actualizando semaforo, barrera y display
                                    //    m_pIntzDAC->SemaforoPaso('v');
                                    //    m_pIntzDAC->AccionarBarrera('s');
                                    //    GetDialogCartel()->Display_c("PAG");
                                    //    //El status debe quedar en EVAbiertaPag
                                    //    m_Estado = EVAbiertaPag;
                                    //}
                                }


                                //Mensaje(DEF_MSG_CLEARDPY, DEF_MSG_DCH);

                                if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                                    Autotabular();

                                //m_pVenta->LimpiarVentaAnterior (false);
                                break;
                        }
                        break;

                    case eEstadoVia.EVAbiertaPag:
                        {
                            switch (respuestaDAC)
                            {
                                //jmrs ver si el pic manda la 'M' y si es asi comentarlo
                                case 'M':
                                    // Retrocedio sin llegar a liberar el Separador
                                    // Vuelvo todo para atras
                                    // (volvemos el semaforo a Verde

                                    Vehiculo oVehiculo = GetVehTran();
                                    //Consulto al PIC los ejes detectados
                                    CategoriaPic(ref oVehiculo, T1, false, null);

                                    // TODO ver si tiene la tarifa
                                    // Se envia mensaje a modulo de video continuo
                                    ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Detectado, null, null, GetVehTran() );


                                    //Si no estamos en modo autista
                                    if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    {
                                        Vehiculo oVehiculoB = GetVehTran();
                                        //Genero un evento de marcha atrás									
                                        EnviarEventoMarchaAtras(ref oVehiculoB, T1, GetVehTran().Categoria, eTipoRetroceso.MarchaAtras_M);
                                    }

                                    _logger.Info("OnSeparSal -> ATRAS (M)");
                                    GetVehTran().Reversa = true;
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                    //Volvemos a mostrar la forma de pago
                                    //m_pInterfaz->SetFormaPago(GetVehTran()->GetFormaPago()); //TODO IMPLEMENTAR
                                    _habiCatego = false;
                                    _flagCatego = true;
                                    break;

                                case 'T':
                                    _logger.Info("OnSeparSal -> ATRAS (T)");
                                    //No está permitido en el estado categorizado!!!!!
                                    // Retrocedio luego de haber liberado el Separador
                                    // Hacemos lo mismo que en caso D
                                    goto case 'D';
                                case 'D':
                                default:
                                    _logger.Info("OnSeparSal -> ADELANTE (D)");


                                    DecideCaptura(eCausaVideo.SeparadorSalidaDesocupado);
                                    Vehiculo oVehiculoA = GetVehTran();
                                    oVehiculoA.Reversa = false;
                                    RegistroTransito(ref oVehiculoA);
                                    //Si todavia estamos imprimiendo el ticket, primero debemos adelantar a pago
                                    if (_logicaCobro.ImprimiendoTicket && !GetVehTran().EstaPagado)
                                    {
                                        AdelantarVehiculo(eMovimiento.eOpPago);
                                        _salidaSinTicket = true;
                                    }
                                    AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                                    //Cambio el estado a libre o categorizado según corresponda
                                    //SeteaEstadosCategorizacion(); //TODO IMPLEMENTAR Como se hace con la lógica de cobro separada

                                    ////Si el vehiculo ingresado tiene tag
                                    //if (GetVehIng()->GetRefInfoTag()->GetTagOK())
                                    //    TagIpicoVerificarManual();

                                    //else if (m_ColaVehPagoAd.GetCount() > 0)
                                    //{
                                    //    //Pasamos el primer vehiculo a vehtran
                                    //    TransferirVehColaPagoAd(GetVehTran());
                                    //    //actualizando semaforo, barrera y display
                                    //    m_pIntzDAC->SemaforoPaso('v');
                                    //    m_pIntzDAC->AccionarBarrera('s');
                                    //    GetDialogCartel()->Display_c("PAG");
                                    //    //El status debe quedar en EVAbiertaPag
                                    //    m_Estado = EVAbiertaPag;
                                    //}

                                    //si ya categoricé y puedo pagar sobre el lazo paso a estado categorizado
                                    if (GetVehIng().Categoria > 0)//&& PagoSobreLazoHabilitado())
                                    {
                                        _logicaCobro.Estado = eEstadoVia.EVAbiertaCat;
                                        //m_pInterfaz->SetChpHabi(true);
                                    }
                                    else
                                        _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;

                                    if (ComitivaVehPendientes > 0)
                                        _logicaCobro.Estado = eEstadoVia.EVAbiertaPag;

                                    //m_pVenta->LimpiarVentaAnterior (false);							
                                    break;


                            }
                            break;
                        }
                }


                /****** MANUAL ******/


                //Si no hubo falla de sensores mando las fallas de lógica que
                //se produjeron (se envian dentro del método GrabarLogSensores)
                //Si no está la vía en sentido opuesto
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                {
                    if (!FallaSensoresDAC(btFiltrarSensores))
                    {
                        if (bSendLogica1) GrabarLogSensores("SeparadorSalidaEgreso -> Adelante", eLogSensores.Logica_Sensores);
                        if (bSendLogica2) GrabarLogSensores("SeparadorSalidaEgreso -> Adelante con Vía Vacía", eLogSensores.Logica_Sensores);
                        if (bSendLogica3) GrabarLogSensores("SeparadorSalidaEgreso -> Vaciar la vía", eLogSensores.Logica_Sensores);
                    }

                }


                if (RespDAC < 0)
                {
                    //TODO IMPLEMENTAR
                    //LogAnomalias.Evento("CMaq_Est::OnSeparSal -> Error en PIC Lazo 2", DEF_LOG_DETALLE);
                    //msgaux.LoadString(IDS_TEXTO98); //"Error en PIC Lazo 2"
                    //Mensaje(msgaux);
                    //CEFallacri* FCEve;
                    //FCEve = new CEFallacri(GetEstacion(), m_NumTCI, &m_Fecha, CEFallacri::FCPic, GetStringPICError(msgaux, lRetDAC), GetNumTurn());
                    //m_pEventosSQL->WriteEvento(FCEve);

                }


                if (respuestaDAC != 'M')
                    _fecUltimoTransito = DateTime.Now;//m_Fecha;

                GrabarVehiculos();



                //MostrarColaVehiculos();
                //UpdatePagoAdelantado();





            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            LoguearColaVehiculos();
            _logger.Info("******************* SeparadorSalidaEgreso -> Salida");

            sw.Stop();
            _loggerTiempos.Info(sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Carga los datos necesarios en pVehiculo para luego generar
        /// el evento de violación
        /// </summary>
        /// <param name="pVehiculo">vehículo sobre el cual se asignan datos para luego generar el evento de violación correspondiente</param>
        /// <param name="bForzar">indica si la violación es forzada o no. Si es forzada no hay que activar las alarmas</param>
        /// <returns></returns>
        private bool Violacion(ref Vehiculo oVehiculo, bool bForzar = false)
        {

            _logger.Info("Violacion -> Inicio");
            byte byEstado = 0;
            DateTime T1 = DateTime.Now;//_fechaTransito; //TODO IMPLEMENTAR
            //bool bRet = false;
            bool ret = false;

            if (ComitivaVehPendientes > 0)
                return ret;
            try
            {
                //Resteo el flag de confirmación de lectura manual
                //TODO IMPLEMENTAR Limpio estado de confirmación el logicacobro
                //if (m_pConfirmaMSG->GetConceptoActual() == CConfirmacion::T_LecturaManualTag)
                //   m_pConfirmaMSG->Limpiar();

                //Envío el evento si en ANT hay un vehículo
                EnviarEventoVehAnt();

                bool semaforoEnVerde = true;
                if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera
                    && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                {
                    semaforoEnVerde = _logicaCobro.ModoPermite(ePermisosModos.SemaforoPasoVerdeEnQuiebre);
                }

                if (!bForzar && !semaforoEnVerde)
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

                //Asigno la fecha del evento
                oVehiculo.Fecha = T1;
                // En Modo D ya tiene una categoría
                CategoriaPic(ref oVehiculo, T1, false, null);

                //Recuerdo la huella
                _huellaDAC = oVehiculo.Huella;

                //En modo MD Solo violacion si hay peanas 
                //y el vehiculo no salio chupado
                if (oVehiculo.InfoDac.HayPeanas
                        && !oVehiculo.SalioChupado)
                {
                    _logger.Info("Se detectaron Peanas");
                    ret = true;

                    //Si estamos en sentido inverso no hacemos nada
                    if (_ultimoSentidoEsOpuesto)
                    {
                        _logger.Info("Violacion en Sentido Inverso");
                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionAutista);
                    }
                    else
                    {
                        if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera)
                        {
                            oVehiculo.ModoBarrera = 'Q';
                        }

                        _logger.Debug("LogicaViaManual::Violacion -> ModoBarrera[{0}]", oVehiculo.ModoBarrera);

                        //Si el vehículo TRAN retrocedió y hay apertura de barrera manual
                        //entonces no es una violación, dejo el estado del transito igual
                        if (!(oVehiculo.Reversa && oVehiculo.ModoBarrera == 'T'))
                        {
                            //Asigno la fecha del evento
                            oVehiculo.Fecha = T1;

                            //Si el vehículo TRAN fue en marcha atrás genero el evento 
                            //de transito simulando una salida del separador si no hay
                            //que forzar la violación
                            if (oVehiculo.Reversa && !bForzar)
                                AdelantarVehiculo(eMovimiento.eSalidaSeparador);

                            // Mensaje de violacion en pantalla
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Violacion");
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "");
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "");
                            _flagIniViol = true;

                            //Si el estado no es quiebre de barrera muestro
                            //la palabra Violación en el display linea 1
                            // sino linea 2 para no pisar Quiebre de Barrera
                            
                            //TODO IMPLEMENTAR
                            if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera)
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Violacion Quiebre");
                               


                            /*//TODO IMPLEMENTAR EL ESTADO
                             * byEstado = _Estado;
                            if (m_Estado == EVModoSup)
                                byEstado = m_PrevEst;
                                */
                            switch (_logicaCobro.Estado)
                            {
                                case eEstadoVia.EVCerrada:
                                    oVehiculo.TipoViolacion = 'R';

                                    ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesPrevias);

                                    break;

                                case eEstadoVia.EVQuiebreBarrera:
                                    //Incremento el total de violaciones por quiebre de barrera
                                    //@TODO falta Quiebre controlado o liberado
                                    if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                                    {
                                        oVehiculo.TipoViolacion = 'Q';
                                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesQuiebreBarrera);
                                    }
                                    else if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreControlado)
                                    {
                                        oVehiculo.TipoViolacion = 'U';
                                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesQuiebreBarrera);
                                    }
                                    break;
                                default:
                                    oVehiculo.TipoViolacion = 'I';
                                    break;
                            }

                            //Capturo foto y video
                            DecideCaptura(eCausaVideo.Violacion);

                            //V:Violacion
                            DecideAlmacenar(eAlmacenaMedio.Violacion, ref oVehiculo);

                            //Incremento el número de transitos
                            oVehiculo.NumeroTransito = IncrementoTransito();
                            //Seteo el tipo de dia hora
                            TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(oVehiculo.InfoDac.Categoria, 0);

                            // Se busca la tarifa
                            Tarifa tarifa = ModuloBaseDatos.Instance.BuscarTarifa(tarifaABuscar);

                            // Si existe una tarifa
                            if (tarifa != null && tarifa.Valor > 0)
                            {
                                oVehiculo.TipoDiaHora = tarifa.CodigoHorario;
                            }

                            //Evento de violación
                            oVehiculo.Operacion = "VI";
                            //Sumo la violación en el turno
                            if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera)
                            {
                                ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.ViolQBL);
                            }
                            else if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                            {
                                if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreControlado)
                                {
                                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.ViolQBC);
                                }
                                else
                                {
                                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Viol);
                                }
                            }

                            bool activarAlarma = _logicaCobro.ModoQuiebre == eQuiebre.Nada ||
                                                (_logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.PermisoModo[ePermisosModos.AlarmaQuiebreLiberado]);
                            //( _logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.BuscarPermisoModo( (int)ePermisosModos.AlarmaQuiebreLiberado, _logicaCobro.Modo.Modo ) );

                            if (activarAlarma)
                            {
                                //Sirena		
                                DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 4000, 4000, /**/true);
                                IniciarTimerApagadoCampana(4000);

                                // Actualiza el estado de los mimicos en pantalla
                                Mimicos mimicos = new Mimicos();
                                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;

                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                            }

                            //m_pInterfaz->SetUltimoticket(pVehiculo->GetNumTr(), m_NroTicketF); //TODO IMPLEMENTAR

                            //Mensaje("Violacion", DEF_MSG_L01); //TODO IMPLEMENTAR                           

                            // TODO ver si tiene la tarifa
                            // Se envia mensaje a modulo de video continuo
                            ModuloVideoContinuo.Instance.EnviarMensaje( eMensajesVideoContinuo.Violacion, null, null, oVehiculo );

                        }
                    }
                }
                else
                {
                    if (oVehiculo.SalioChupado)
                    {
                        _logger.Info("Violacion->Salida de Vehiculo chupado");
                        //TODO Generar un evento
                        EnviarEventoMarchaAtras(ref oVehiculo, DateTime.Now /* m_Fecha*/, oVehiculo.Categoria, eTipoRetroceso.MarchaAtras_C);
                    }
                    else
                    {
                        _logger.Info("No se detectaron Peanas");
                    }
                    //Como no mandamos la violacion, lo limpiamos para que no se mande despues
                    //pVehiculo->Clear(true);
                    oVehiculo = new Vehiculo();
                }

                _estadoAbort = 'V';
                //_flagIniViol = false;


                _loggerTransitos?.Info( $"V;{T1.ToString( "HH:mm:ss.ff" )};{oVehiculo.InfoDac.Categoria}" );


                GrabarVehiculos();
                ModuloBaseDatos.Instance.AlmacenarTransitoTurno(oVehiculo, _logicaCobro.GetTurno);
                /*
                //Por ahora grabamos el Log si hay violaciones
                GrabarLogSensores("Violacion",DEF_EVENTO_SENSORES);
                */

                if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                    Autotabular();

            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            _logger.Info("Violacion -> Fín");
            return ret;
        }


        private TarifaABuscar GenerarTarifaABuscar(short categoria, byte? tipoTarifa)
        {
            TarifaABuscar tarifaABuscar = new TarifaABuscar();
            DateTime Fecha = DateTime.Now;

            if (tarifaABuscar != null)
            {
                tarifaABuscar.Catego = categoria;
                tarifaABuscar.Estacion = ModuloBaseDatos.Instance.ConfigVia.CodigoEstacion.GetValueOrDefault();
                tarifaABuscar.TipoTarifa = tipoTarifa;
                tarifaABuscar.FechAct = Fecha;
                tarifaABuscar.FechaComparacion = Fecha;
                tarifaABuscar.FechVal = Fecha;
                tarifaABuscar.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido;
                /* tarifaABuscar.AfectaDetraccion = afectaDetraccion;
                 tarifaABuscar.ValorDetraccion = valorDetraccion;*/
            }

            return tarifaABuscar;
        }
        private void IniciarTimerApagadoCampana(int tiempoMseg)
        {
            //Timer de apagado de mimico de campana de violacion
            _timerApagadoCampana.Elapsed += new ElapsedEventHandler(TimerApagadoCampana);
            _timerApagadoCampana.Interval = tiempoMseg;
            _timerApagadoCampana.AutoReset = false;
            _timerApagadoCampana.Enabled = true;
        }        

        private void TimerApagadoCampana(object source, ElapsedEventArgs e)
        {
            _timerApagadoCampana.Enabled = false;
            // Actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            mimicos.CampanaViolacion = enmEstadoAlarma.Ok;

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
        }

        private void IniciarTimerLazoSalida()
        {
            //Timer de apagado de mimico de campana de violacion
            _timerFotoLazoSalida.Elapsed += new ElapsedEventHandler(TimerLazoSalida);
            _timerFotoLazoSalida.Interval = 1000;
            _timerFotoLazoSalida.AutoReset = true;
            _timerFotoLazoSalida.Enabled = true;
            _timerFotoLazoSalida.Start();
        }

        private void DetenerTimerLazoSalida()
        {
            //Timer de apagado de mimico de campana de violacion
            _timerFotoLazoSalida.AutoReset = false;
            _timerFotoLazoSalida.Enabled = false;
            _timerFotoLazoSalida.Stop();
        }

        private void TimerLazoSalida(object source, ElapsedEventArgs e)
        {
            Vehiculo vehiculo = GetVehTran();
            if (!vehiculo.EstaPagado)
                vehiculo = GetVehAnt();
            
            eCausaVideo causaVideo = eCausaVideo.LazoSalidaDesocupado;
            InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(), eCamara.Lateral, eTipoMedio.Foto, causaVideo, false);
            vehiculo.ListaInfoFoto.Add(oFoto);
            ModuloFoto.Instance.SacarFoto(vehiculo, causaVideo, false, oFoto);

            if (vehiculo.ListaInfoFoto.Count > 10)
                DetenerTimerLazoSalida();
        }

        /// <summary>
        ///  Genera el evento de transito con vehAnt
        /// </summary>
        private void EnviarEventoVehAnt()
        {
            //if (GetVehAnt().Init)
            //if (GetVehAnt().EstaPagado)
                GeneraEventosTransito();
        }



        /// <summary>
        /// Genera un evento de marcha atrás
        /// </summary>
        /// <param name="oVehiculo">referencia al vehículo que hizo marcha atrás</param>
        /// <param name="fecha">fecha del evento</param>
        /// <param name="catego">categoría del tránsito</param>
        /// <param name="tipoRetroceso">tipo de retroceso (MA o AT)</param>
        public void EnviarEventoMarchaAtras(ref Vehiculo oVehiculo, DateTime fecha,
                                               short catego, eTipoRetroceso tipoRetroceso)
        {
            //TODO IMPLEMENTAR
            //CEvento* eve = NULL;
            char cTipOp, cTipBo;

            try
            {
                //Si el vehículo no tiene tipo de operación es una violación
                //según el estado de la vía asigno el código
                if (oVehiculo.TipOp == ' ')
                {
                    switch (_logicaCobro.Estado)
                    {
                        case eEstadoVia.EVCerrada: //Violación por vía cerrada
                            cTipOp = 'R';
                            break;

                        case eEstadoVia.EVQuiebreBarrera:  //Quiebre de barrera
                            if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                                cTipOp = 'Q';   //quiebre liberado
                            else
                                cTipOp = 'U';   //quiebre controlado
                            break;

                        default:    //Violación por vía abierta
                            if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreControlado)
                                cTipOp = 'U';
                            else
                                cTipOp = 'I';
                            break;
                    }
                    //TipBo es ' '
                    cTipBo = ' ';
                }
                else
                {
                    cTipOp = oVehiculo.TipOp;
                    cTipBo = oVehiculo.TipBo;
                }

                //TODO IMPLEMENTAR
                //eve = new CEMarchaAtras(GetEstacion(), GetNumTCI(),
                //                       &dFecha, m_NumTurn,
                //                       m_sOperadorID, GetSentido(),
                //                       cTipOp, cTipBo,
                //                       ucCatego, pVehiculo->GetHuella(),
                //                       pVehiculo->GetNumTr(), m_Mantenimiento,
                //                       sTipoRetroceso);
                //m_pEventosSQL->WriteEvento(eve);

                //Salvamos la ultima huella
                _huellaDAC = oVehiculo.Huella;
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }


        /// <summary>
        /// Obtiene todo los contadores de ejes del DAC y calcula la categoría en base a lo que esta configurado en la base de datos
        /// </summary>
        /// <param name="oVehiculo">Vehículo al cual asignar todos los datos</param>
        /// <param name="T1">Hora de los eventos de fallas que genera esta funcion</param>
        /// <param name="bGenerarFallas">Si es falso no se generan eventos de falla</param>
        /// <param name="oRegDac"></param>
        private void CategoriaPic(ref Vehiculo oVehiculo, DateTime T1, bool bGenerarFallas, RegistroDacMD oRegDac)
        {
            string nada = "", csDacTot = "";
            int flag = 0;
            char Moto = 'N';    //Indica si el tránsito es una moto
                                //Fecha para todos los eventos durante el tránsito, a pedido de Jach!!!!!!!!

            //CEvento* evento;
            byte max1 = 0;
            byte max2 = 0;
            byte maxtot = 0;
            byte DACalt = 0;

            byte byCantEjes1 = 0, byCantEjes2 = 0, byCantEjes3 = 0, byCantEjes4 = 0, byCantEjes5 = 0, byCantEjes6 = 0, byCantEjes7 = 0, byCantEjes8 = 0;
            byte byRueDobles57 = 0, byRueDobles68 = 0, byAdelante57 = 0, byAdelante68 = 0, byAtras57 = 0, byAtras68 = 0;
            byte byCambSenti57 = 0, byCambSenti68 = 0, byAltura = 0, byEjesLevantados = 0;
            bool bOKEjes1 = false, bOKEjes2 = false, bOKEjes3 = false, bOKEjes4 = false;
            bool bUso1 = false;

            //Analizo la configuración de la via
            char cejes = ' ';
            char cdobl = ' ';
            char altur = ' ';
            char ejesl = ' ';

            byte btDACrdobles = 0;  //Contador de ruedas dobles 57 
            byte btDACrdobles2 = 0; //Contador de ruedas dobles 68
            byte btDACejes = 0;     //Contador de ejes 1
            byte btDACejes2 = 0;        //Contador de ejes 2
            byte btDACejes3 = 0;        //Contador de ejes 3
            byte btDACejes4 = 0;        //Contador de ejes 4
            byte btBus_Cejes = 0;       //Cantidad de ejes
            byte btBus_CejesTotales = 0; //Cantidad de ejes a buscar en el DAC
            byte btBus_Rdobles = 0; //Cantidad de ruedas duales
            char btBus_Alt = 'B';       //Altura del vehículo
            short shCatego_Dac = 0; //Categoría del DAC
            bool bHayPeanas = false;    //Hay deteccion de alguna peana?


            ContadoresDAC contadores = new ContadoresDAC();

            _contSuspensionTurno = 0;

            _logger.Info("CategoriaPIC -> Inicio");

            //Si el vehiculo ya tiene asignada fecha, uso esa fecha para los eventos
            if (oVehiculo.Fecha > DateTime.MinValue)
                T1 = oVehiculo.Fecha;

            //Si el modo es D o autotabulante la vía no tiene peanas, no consulto al DAC y devuelvo
            //como categoría del DAC el valor de la categoría autotabulada
            if (_sinPeanas)
            {
                oVehiculo.InfoDac.Categoria = _catego_Autotabular;
                oVehiculo.InfoDac.HayPeanas = true;
                _logger.Info("CategoriaPIC -> Modo D o autotabulante, no consulto DAC");
            }
            else
            {
                ConfigVia configVia = ModuloBaseDatos.Instance.ConfigVia;
                cejes = configVia.ContadorEjes;
                cdobl = configVia.DetectorRuedasDobles;
                altur = configVia.DetectorAltura;
                ejesl = configVia.CobraEjesLevantados;

                if (oRegDac == null)
                {
                    _logger.Info("CategoriaPIC -> Modo<>D y no autotabulante, consulto DAC");
                    // @TODO
                    // Falta descontar los ejes para atras



                    //Analizo la cantidad de contadores de ejes
                    DAC_PlacaIO.Instance.ObtenerContadores(cejes, cdobl, altur, ref contadores, ejesl);
                    byCantEjes1 = contadores.Cont_Eje1;
                    byCantEjes2 = contadores.Cont_Eje2;
                    byCantEjes3 = contadores.Cont_Eje3;
                    byCantEjes4 = contadores.Cont_Eje4;
                    byCantEjes5 = contadores.Cont_Eje5;
                    byCantEjes6 = contadores.Cont_Eje6;
                    byCantEjes7 = contadores.Cont_Eje7;
                    byCantEjes8 = contadores.Cont_Eje8;
                    byRueDobles57 = contadores.RueDob57;
                    byRueDobles68 = contadores.RueDob68;
                    byAdelante57 = contadores.Adelante57;
                    byAdelante68 = contadores.Adelante68;
                    byAtras57 = contadores.Atras57;
                    byAtras68 = contadores.Atras68;
                    byCambSenti57 = contadores.CambSenti57;
                    byCambSenti68 = contadores.CambSenti68;
                    byAltura = contadores.Altura;
                    byEjesLevantados = contadores.ContadorEjesLevantados;
                }
                else
                {
                    _logger.Info("CategoriaPIC -> Modo<>D y no autotabulante, consulto Registro de Violación con Vía cerrada");
                    byCantEjes1 = oRegDac.peana1;
                    byCantEjes2 = oRegDac.peana2;
                    byCantEjes3 = oRegDac.peana3;
                    byCantEjes4 = oRegDac.peana4;
                    byCantEjes5 = 0;    //No lo tengo en el registro
                    byCantEjes6 = 0;    //No lo tengo en el registro
                    byCantEjes7 = 0;    //No lo tengo en el registro
                    byCantEjes8 = 0;    //No lo tengo en el registro
                    byRueDobles57 = oRegDac.rdo57;
                    byRueDobles68 = oRegDac.rdo68;
                    byAdelante57 = 0;   //No lo tengo en el registro
                    byAdelante68 = 0;   //No lo tengo en el registro
                    byAtras57 = 0;      //No lo tengo en el registro
                    byAtras68 = 0;      //No lo tengo en el registro
                    byCambSenti57 = 0;  //No lo tengo en el registro
                    byCambSenti68 = 0;  //No lo tengo en el registro
                    byAltura = oRegDac.altura;
                }
                //Huella DAC
                //Maximo 64 chars
                csDacTot = string.Format("EJ[{0}-{1}-{2}-{3}-{4}-{5}-{6}-{7}]RD[{8}-{9}]AD[{10}-{11}]AT[{12}-{13}]CS[{14}-{15}]Alt[{16}]",
                                byCantEjes1, byCantEjes2, byCantEjes3, byCantEjes4, byCantEjes5, byCantEjes6, byCantEjes7, byCantEjes8,
                                byRueDobles57, byRueDobles68, byAdelante57, byAdelante68, byAtras57, byAtras68,
                                byCambSenti57, byCambSenti68, byAltura);
                _logger.Info(csDacTot);



                //Hay peanas?
                if (byCantEjes1 > 0 || byCantEjes2 > 0 || byCantEjes3 > 0 || byCantEjes4 > 0
                    || byCantEjes5 > 0 || byCantEjes6 > 0 || byCantEjes7 > 0 || byCantEjes8 > 0)
                    bHayPeanas = true;

                /////////////////////////	
                //MOTO //////////////////
                ////////////////////////
                //Me fijo si está pasando una moto
                // 1, 3, 5 y 7 todos <= 2 y alguna > 0 y 2, 4, 6 y 8 = 0 
                // o al reves
                // @TODO JAS
                // o 1 y 2 <= 2 y 5, 6, 7, 8

                if (cejes != '2')
                {

                    if (((byCantEjes1 <= 2 && byCantEjes3 <= 2 && byCantEjes5 <= 2 && byCantEjes7 <= 2) &&
                        (byCantEjes1 > 0 || byCantEjes3 > 0 || byCantEjes5 > 0 || byCantEjes7 > 0) &&
                        (byCantEjes2 == 0 && byCantEjes4 == 0 && byCantEjes6 == 0 && byCantEjes8 == 0)) ||
                        ((byCantEjes2 <= 2 && byCantEjes4 <= 2 && byCantEjes6 <= 2 && byCantEjes8 <= 2) &&
                        (byCantEjes2 > 0 || byCantEjes4 > 0 || byCantEjes6 > 0 || byCantEjes8 > 0) &&
                        (byCantEjes1 == 0 && byCantEjes3 == 0 && byCantEjes5 == 0 && byCantEjes7 == 0)) ||
                        ((byCantEjes1 <= 2 && byCantEjes2 <= 2 && byCantEjes5 == 0 && byCantEjes7 == 0 &&
                        byCantEjes3 == 0 && byCantEjes4 == 0 && byCantEjes6 == 0 && byCantEjes8 == 0) &&
                        (byCantEjes1 > 0 || byCantEjes2 > 0)))
                    {
                        //Posible Moto
                        Moto = 'S';
                    }
                    else
                    {
                        Moto = 'N';
                    }
                }
                else
                {
                    Moto = 'N';
                }
                //@TODO JAS
                // Si la cantidad de ejes del contador y los RD
                // son iguales, descontamos los ejes para atras
                // @TODO ver si descontamos algun eje si hay diferencia
                // NO HACEMOS EL DESCUENTO 
                // PORQUE POR LA POSICION DE LAS PEANAS EN COVISUR
                // NO SIEMPRE DETECTA OK

                //son unsigned, comparar diferente
                if (byCantEjes1 == byCantEjes5 && byCantEjes1 == byCantEjes7)
                {
                    bOKEjes1 = true;
                }

                if (byCantEjes2 == byCantEjes6 && byCantEjes2 == byCantEjes8)
                {
                    bOKEjes2 = true;
                }

                if (byCantEjes3 == byCantEjes5 && byCantEjes3 == byCantEjes7)
                {
                    bOKEjes3 = true;
                }

                if (byCantEjes4 == byCantEjes6 && byCantEjes4 == byCantEjes8)
                {
                    bOKEjes4 = true;
                }

                switch (cejes)
                {
                    case '0':
                        {
                            //Sin contador de ejes
                            btDACejes = 0;
                            btBus_Cejes = 0;
                        }
                        break;
                    case '1':
                        {
                            //1 contador de ejes	
                            btDACejes = byCantEjes1;
                            if (btDACejes < 2)
                            {
                            }

                        }
                        break;
                    case '2':
                        {
                            //2 contadores de ejes PARALELOS
                            btDACejes = byCantEjes1;
                            btDACejes2 = byCantEjes3;
                            if (btDACejes < 2)
                            {
                            }
                            if (btDACejes2 < 2)
                            {
                            }

                            if ((btDACejes < 2) && (btDACejes2 < 2))
                            {
                                btBus_Cejes = 0;
                            }

                            if (btDACejes == btDACejes2)
                            {
                                //Si los contadores dieron igual, mando cualquiera de los 2
                                btBus_Cejes = btDACejes;
                            }
                            else
                            {
                                //Si bOKEjes1 es true y bOKEjes3 es false y ejes1 >= 2 uso el 1
                                //Si bOKEjes1 es false y bOKEjes3 es true y ejes3 >= 2 uso el 3
                                // sino tomo el mayor
                                if ((bOKEjes1 && !bOKEjes3 && btDACejes >= 2))
                                    bUso1 = true;
                                else if ((!bOKEjes1 && bOKEjes3 && btDACejes2 >= 2))
                                    bUso1 = false;
                                else if ((btDACejes > btDACejes2))
                                    bUso1 = true;
                                else
                                    bUso1 = false;

                                if (bUso1)
                                {
                                    btBus_Cejes = btDACejes;
                                }
                                else
                                {
                                    btBus_Cejes = btDACejes2;
                                }
                            }
                        }
                        break;
                    case '3':
                        {
                            //2 contadores de ejes ALINEADOS
                            btDACejes = byCantEjes1;
                            btDACejes2 = byCantEjes2;
                            if (btDACejes < 2)
                            {
                            }
                            if (btDACejes2 < 2)
                            {
                            }
                            //Si ninguno detecto tomo 0
                            if ((btDACejes < 1) && (btDACejes2 < 1))
                            {
                                btBus_Cejes = 0;
                            }
                            //Si alguno detecto 1 eje y ninguno 2 o mas tomo 2 ejes
                            else if ((btDACejes < 2) && (btDACejes2 < 2))
                            {
                                btBus_Cejes = 2;
                            }
                            else
                            {

                                if (btDACejes == btDACejes2)
                                {
                                    //Si los contadores dieron igual, mando cualquiera de los 2
                                    btBus_Cejes = btDACejes;
                                }
                                else
                                {
                                    //Si bOKEjes1 es true y bOKEjes2 es false y ejes1 >= 2 uso el 1
                                    //Si bOKEjes1 es false y bOKEjes2 es true y ejes2 >= 2 uso el 2
                                    // sino tomo el mayor
                                    if ((bOKEjes1 && !bOKEjes2 && btDACejes >= 2))
                                        bUso1 = true;
                                    else if ((!bOKEjes1 && bOKEjes2 && btDACejes2 >= 2))
                                        bUso1 = false;
                                    else if ((btDACejes > btDACejes2))
                                        bUso1 = true;
                                    else
                                        bUso1 = false;

                                    if (bUso1)
                                    {
                                        btBus_Cejes = btDACejes;
                                    }
                                    else
                                    {
                                        btBus_Cejes = btDACejes2;
                                    }
                                }
                            }
                        }
                        break;
                    case '4':
                        {
                            //4 contadores de ejes 
                            btDACejes = byCantEjes1;
                            btDACejes2 = byCantEjes2;
                            btDACejes3 = byCantEjes3;
                            btDACejes4 = byCantEjes4;
                            if (btDACejes < 2)
                            {
                            }
                            if (btDACejes2 < 2)
                            {
                            }
                            if (btDACejes3 < 2)
                            {
                            }
                            if (btDACejes4 < 2)
                            {
                            }
                            //Si ninguno detecto ejes tomo 0
                            if ((btDACejes < 1) && (btDACejes2 < 1) && (btDACejes3 < 1) && (btDACejes4 < 1))
                            {
                                btBus_Cejes = 0;
                            }
                            //Si alguno detecto 1 eje y ninguno 2 o mas tomo 2 ejes
                            else if ((btDACejes < 2) && (btDACejes2 < 2) && (btDACejes3 < 2) && (btDACejes4 < 2))
                            {
                                btBus_Cejes = 2;
                            }

                            else
                            {
                                flag = 0;
                                //Si hay 3 contadores iguales con valor distinto a 0 tomo ese valor
                                //1, 3 y 4
                                if (btDACejes == btDACejes3 && btDACejes == btDACejes4 && btDACejes > 0)
                                {
                                    btBus_Cejes = btDACejes;
                                    flag = 1;
                                }
                                //1, 2 y 4
                                else if (btDACejes == btDACejes2 && btDACejes == btDACejes4 && btDACejes > 0)
                                {
                                    btBus_Cejes = btDACejes;
                                    flag = 1;
                                }
                                //1, 2 y 3
                                else if (btDACejes == btDACejes2 && btDACejes == btDACejes3 && btDACejes > 0)
                                {
                                    btBus_Cejes = btDACejes;
                                    flag = 1;
                                }
                                //2, 3 y 4
                                else if (btDACejes2 == btDACejes3 && btDACejes2 == btDACejes4 && btDACejes2 > 0)
                                {
                                    btBus_Cejes = btDACejes2;
                                    flag = 1;
                                }
                                if (flag == 0)
                                {
                                    //Si no hay un trio de contadores iguales saco el mayor 
                                    //contador de los 4
                                    if (btDACejes >= btDACejes2)
                                        max1 = btDACejes;
                                    else
                                        max1 = btDACejes2;
                                    if (btDACejes3 >= btDACejes4)
                                        max2 = btDACejes3;
                                    else
                                        max2 = btDACejes4;
                                    if (max1 >= max2)
                                        maxtot = max1;
                                    else
                                        maxtot = max2;
                                    btBus_Cejes = maxtot;
                                }
                            }
                        }
                        break;
                    case '5':
                        {
                            //2 contadores de ejes PARALELOS y 1 alineado
                            btDACejes = byCantEjes1;
                            btDACejes2 = byCantEjes2;
                            btDACejes3 = byCantEjes3;

                            //Me fijo si está pasando una moto
                            if (Moto == 'S')
                            {   //Es probable que sea una moto				
                                //Igual calculo la cantidad de ejes que tiene
                                //por si no es una moto
                                //Me fijo si DACejes es el mayor
                                if ((btDACejes > btDACejes2) && (btDACejes > btDACejes3))
                                    btBus_Cejes = btDACejes;    //DACejes es el mayor
                                else
                                {   //Me fijo si DACejes2 es el mayor
                                    if ((btDACejes2 > btDACejes) && (btDACejes2 > btDACejes3))
                                        btBus_Cejes = btDACejes2;   //DACejes2 es el mayor
                                    else
                                        btBus_Cejes = btDACejes3;   //DACejes3 es el mayor
                                }

                                //Me fijo si piso las pedaleras 1 o 3 
                                if (btDACejes > 0 || btDACejes3 > 0)
                                {
                                    //Me fijo si alguno de los contadores contó 1
                                    if (btDACejes < 2)
                                    {
                                    }
                                    if (btDACejes3 < 2)
                                    {
                                    }
                                }
                                else
                                {
                                }
                            }
                            else
                            {   //NO ES UNA MOTO
                                //Si todos contaron menos de 2 ejes no se detecto
                                //un vehiculo
                                if ((btDACejes < 2) && (btDACejes2 < 2) && (btDACejes3 < 2))
                                {
                                    btBus_Cejes = 0;
                                }
                                else
                                {
                                    if ((btDACejes == btDACejes2) && (btDACejes == btDACejes3))
                                    {
                                        //Si los contadores dieron igual, mando cualquiera de los 3
                                        btBus_Cejes = btDACejes;
                                    }
                                    else
                                    {   //Me fijo si DACejes es el mayor
                                        if ((btDACejes >= btDACejes2) && (btDACejes >= btDACejes3))
                                        {
                                            //DACejes tiene el mayor valor
                                            btBus_Cejes = btDACejes;
                                        }
                                        else
                                        {   //Me fijo si DACejes2 es el mayor
                                            if ((btDACejes2 >= btDACejes) && (btDACejes2 >= btDACejes3))
                                            {   //DACEjes2 es el mayor
                                                btBus_Cejes = btDACejes2;
                                            }
                                            else
                                            {   //DACEjes3 es el mayor
                                                btBus_Cejes = btDACejes3;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    default:
                        {

                            _logger.Info("Parametro Contadores de Ejes INVALIDO. Configuración Erronea");
                            btBus_Cejes = 0;
                        }
                        break;
                }
                btBus_CejesTotales = btBus_Cejes;
                if (ejesl == 'S')
                    btBus_CejesTotales += byEjesLevantados;
                
                ///////////////
                //ALTURA //////
                ///////////////
                switch (altur)
                {
                    case 'S':
                        {
                            //Si tiene altura y esta en fallo, genero evento
                            _falloAltura = DAC_PlacaIO.Instance.ObtenerFalloAltura();
                            DACalt = byAltura;
                            if (DACalt == 0 || _falloAltura)
                            {
                                btBus_Alt = 'B';
                            }
                            else
                            {
                                btBus_Alt = 'A';
                            }
                        }
                        break;
                    case 'N':
                        {
                            btBus_Alt = 'B';
                            _falloAltura = false;
                        }
                        break;
                    default:
                        {
                            _falloAltura = false;
                            _logger.Info("Parametro Sensor de Altura INVALIDO.Configuración Erronea");
                            btBus_Alt = 'B';
                            break;
                        }

                }
                ///////////////
                //DOBLES //////
                ///////////////
                byte valor = 0;

                switch (cdobl)
                {
                    case '2':
                        {	
                            btDACrdobles = byRueDobles57;
                            btDACrdobles2 = byRueDobles68;
                            if (btDACrdobles == 0 && btDACrdobles2 == 0)
                            {
                                btBus_Rdobles = 0;
                            }
                            else if (btDACrdobles > btDACrdobles2)
                            {
                                btBus_Rdobles = btDACrdobles;
                            }
                            else
                            {
                                btBus_Rdobles = btDACrdobles2;
                            }

                            //Solamente generamos falla de peanas duales si hay 3 ejes o mas
                            if (btBus_Cejes > 2)
                            {
                                if (byCantEjes1 > 0 || byCantEjes5 > 0 || byCantEjes7 > 0)
                                {
                                    valor = byCantEjes5;
                                    valor = byCantEjes7;
                                }
                                if (byCantEjes2 > 0 || byCantEjes6 > 0 || byCantEjes8 > 0)
                                {
                                    valor = byCantEjes6;
                                    valor = byCantEjes8;
                                }
                            }
                        }
                        break;

                    case '1':
                        {	
                            btDACrdobles = byRueDobles57;
                            if (btDACrdobles == 0)
                            {
                                btBus_Rdobles = 0;
                            }
                            else
                            {
                                btBus_Rdobles = btDACrdobles;
                            }

                            //Solamente generamos falla de peanas duales si hay 3 ejes o mas
                            if (btBus_Cejes > 2)
                            {
                                if (byCantEjes1 > 0 || byCantEjes5 > 0 || byCantEjes7 > 0)
                                {
                                    valor = byCantEjes5;
                                    valor = byCantEjes7;
                                }
                            }
                        }
                        break;

                    default:
                        {

                            _logger.Info("Parametro Sensor de Ruedas Dobles INVALIDO. Configuración Erronea");
                            btBus_Rdobles = 0;
                            break;
                        }

                }

                //Busco en Base de CatDac
                // Cat,Ejes,Rdas Dobles,Altura
                //Rdas Dobles y Altura > 0 ='S'
                //Fallas de categorización
                //		-> + de 9 ejes
                //		-> - de 2 ejes y que no sea moto

                //Si no detecte ruedas dobles y detecte una posible moto
                //vacio el contador de ejes, sino saco el flag de detección de motos
                if ((Moto == 'S') && (btBus_Rdobles == 0))
                {   
                }
                else
                {   //No es moto, saco el flag de moto
                    Moto = 'N';
                }

                //Si detecto un solo eje lo buscamos como 2 ejes
                if (((btBus_CejesTotales < 1) && (Moto == 'N')) || (btBus_CejesTotales > 9))
                {

                    //No categorizo falla en sensor
                    if (btBus_CejesTotales < 2)
                    {
                        shCatego_Dac = -2;
                    }

                    //No categorizo falla en lazo
                    if (btBus_CejesTotales > 9)
                    {
                        shCatego_Dac = -1;
                    }

                    if (cejes == 0)
                        shCatego_Dac = 0;

                }
                else
                {
                    byte auxBusrdobles = 0;
                    byte auxBusejes = 0;

                    if (btBus_Rdobles == 0)
                        auxBusrdobles = 0;
                    else
                        auxBusrdobles = 1;

                    if (Moto == 'S')
                        auxBusejes = 0;
                    else if (btBus_CejesTotales == 1)
                        auxBusejes = 2;
                    else
                        auxBusejes = btBus_CejesTotales;


                    //@@TODO falla sensor de altura
                    //Si no tiene detector duales y fallo la altura
                    //reviso si con y sin altura es la misma categoria
                    short categoria = 0;
                    if (cdobl == '0'
                        && _falloAltura)
                    {
                        /////////////////////////////////////////////////////
                        //FALLA DE SENSOR DE ALTURA//////////////////////////
                        /////////////////////////////////////////////////////
                        short catdacbajo = -2, catdacalto = -2;
                        CatDacABuscar oCatDacaBuscarBajo = new CatDacABuscar() { Altura = 'B', Ejes = auxBusejes, RuedasDobles = auxBusrdobles, Motos = Moto };
                        CatDacABuscar oCatDacaBuscarAlto = new CatDacABuscar() { Altura = 'A', Ejes = auxBusejes, RuedasDobles = auxBusrdobles, Motos = Moto };

                        //Busco categoria Bajo
                        if (BuscarCategoriasDAC(ref categoria, oCatDacaBuscarBajo))
                        {
                            catdacbajo = categoria;
                        }
                        //Busco categoria Alto
                        if (BuscarCategoriasDAC(ref categoria, oCatDacaBuscarAlto))
                        {
                            catdacalto = categoria;
                        }

                        //Si son iguales, informo esta categoria
                        //Sino -2 (no categorizo por falla en sensor)
                        if (catdacalto == catdacbajo)
                        {
                            shCatego_Dac = catdacalto;
                        }
                        else
                        {
                            shCatego_Dac = -2;
                        }
                    }
                    else
                    {
                        CatDacABuscar oCatDacaBuscar = new CatDacABuscar() { Altura = btBus_Alt, Ejes = auxBusejes, RuedasDobles = auxBusrdobles, Motos = Moto };
                        if (BuscarCategoriasDAC(ref categoria, oCatDacaBuscar))
                            shCatego_Dac = categoria;
                        else
                        {
                            shCatego_Dac = 0;
                            _logger.Info("Categoria DAC no definida");
                        }
                    }
                }


                csDacTot = string.Empty;
                csDacTot = string.Format("EJ[{0}-{1}-{2}-{3}-{4}-{5}-{6}-{7}]RD[{8}-{9}]AD[{10}-{11}]AT[{12}-{13}]CS[{14}-{15}]Alt[{16}]",
                                btDACejes, btDACejes2, btDACejes3, btDACejes4, byCantEjes5, byCantEjes6, byCantEjes7, byCantEjes8,
                                btDACrdobles, btDACrdobles2, byAdelante57, byAdelante68, byAtras57, byAtras68,
                                byCambSenti57, byCambSenti68, btBus_Alt == 'B' ? '0' : '1');
                //Asigno la cantidad de ejes, ruedas duales, altura y la categoría DAC
                //al vehículo trans
                oVehiculo.InfoDac.Categoria = shCatego_Dac;
                oVehiculo.InfoDac.Altura = btBus_Alt;
                oVehiculo.InfoDac.CantidadEjes = btBus_Cejes;
                oVehiculo.InfoDac.CantidadRuedasDuales = btBus_Rdobles;
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje1, btDACejes);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje2, btDACejes2);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje3, btDACejes3);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje4, btDACejes4);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje5, byCantEjes5);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje6, byCantEjes6);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje7, byCantEjes7);
                oVehiculo.InfoDac.SetSensorEjes(Peanas.Eje8, byCantEjes8);
                oVehiculo.InfoDac.SetSensorRDual(PeanasConjunto.Eje57, btDACrdobles);
                oVehiculo.InfoDac.SetSensorRDual(PeanasConjunto.Eje68, btDACrdobles2);
                oVehiculo.InfoDac.HayPeanas = bHayPeanas;
                oVehiculo.InfoDac.YaTieneDAC = true;
                oVehiculo.Huella = csDacTot;
                oVehiculo.InfoDac.CantidadEjesLevantados = byEjesLevantados;
            }

            _logger.Info("CategoriaPIC -> Fin[{Name}]", shCatego_Dac);
            _falloAltura = false; //lo limpio por las dudas
        }

        private bool BuscarCategoriasDAC(ref short categoria, CatDacABuscar oCatDacaBuscar)
        {
            bool ret = false;

            CatDac oCatDac = ModuloBaseDatos.Instance.BuscarCategoriasDAC(oCatDacaBuscar);
            categoria = oCatDac.Categoria;

            if (categoria != 0)
                ret = true;
            else
            {
                //Fallo la consulta
                if (oCatDacaBuscar.Ejes == 1 && oCatDacaBuscar.RuedasDobles == 0)
                {
                    categoria = 1;
                }
                else if (oCatDacaBuscar.Ejes == 2 && oCatDacaBuscar.RuedasDobles == 0 && oCatDacaBuscar.Altura == 'B')
                {
                    categoria = 2;
                }
                else if (oCatDacaBuscar.Ejes == 2 && oCatDacaBuscar.RuedasDobles == 1 && oCatDacaBuscar.Altura == 'B')
                {
                    categoria = 2;
                }
                else if (oCatDacaBuscar.Ejes == 2 && oCatDacaBuscar.RuedasDobles == 0 && oCatDacaBuscar.Altura == 'A')
                {
                    categoria = 2;
                }
                else if (oCatDacaBuscar.Ejes == 2 && oCatDacaBuscar.RuedasDobles == 1 && oCatDacaBuscar.Altura == 'A')
                {
                    categoria = 2;
                }
                else if (oCatDacaBuscar.Ejes == 3 && oCatDacaBuscar.RuedasDobles == 0 && oCatDacaBuscar.Altura == 'B')
                {
                    categoria = 9;
                }
                else if (oCatDacaBuscar.Ejes == 3 && oCatDacaBuscar.RuedasDobles == 1 && oCatDacaBuscar.Altura == 'B')
                {
                    categoria = 3;
                }
                else if (oCatDacaBuscar.Ejes == 3 && oCatDacaBuscar.RuedasDobles == 0 && oCatDacaBuscar.Altura == 'A')
                {
                    categoria = 3;
                }
                else if (oCatDacaBuscar.Ejes == 3 && oCatDacaBuscar.RuedasDobles == 1 && oCatDacaBuscar.Altura == 'A')
                {
                    categoria = 3;
                }
                else
                {
                    categoria = -1;
                }
            }
            return ret;
        }

        /// <summary>
        /// Pasa el vehiculo ANT a TRA y borra el contenido de ANT
        /// </summary>
        private void RetrocederVehiculo()
        {
            _logger.Info("RetrocederVehiculo");

            //Muevo el vehículo ANT a TRA
            _vVehiculo[(int)eVehiculo.eVehTra] = _vVehiculo[(int)eVehiculo.eVehAnt];
            //Asigno TRAN a ONLINE
            _vVehiculo[(int)eVehiculo.eVehOnLine] = _vVehiculo[(int)eVehiculo.eVehTra];
            //Limpio ANT
            _vVehiculo[(int)eVehiculo.eVehAnt] = new Vehiculo();
            //MostrarColaVehiculos();
            //UpdatePagoAdelantado();
        }

        public override void AdelantarVehiculosModoD()
        {

        }

        /// <summary>
        /// Mueve el vehículo correspondiente de acuerdo al tipo de movimiento
        /// No tiene efecto cuando la vía está en modo D
        /// </summary>
        /// <param name="eMovim">tipo de movimiento</param>
        public override void AdelantarVehiculo(eMovimiento movimiento)
        {
            //No es válido para el modo D


            switch (movimiento)
            {
                case eMovimiento.eOpPago:
                    _logger.Info("AdelantarVehiculo -> eOpPago");
                    //Si TRAN está ocupado y no es violacion lo muevo a ANT y genero el evento
                    if (GetVehTran().EstaPagado)
                    {
                        if (GetVehTran().Operacion != "VI")
                            AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                        else
                        {
                            //Asigno a ING el número de transito de TRAN para que no vuelva
                            //a pedir uno nuevo ya que la violación desaparece
                            GetVehIng().NumeroTransito = GetVehTran().NumeroTransito;
                            //m_pInterfaz->SetUltimoticket(GetVehIng()->GetNumTr(), 0); //TODO IMPLEMENTAR
                        }
                    }
                    //Muevo el vehículo ING a TRAN, si Ant está ocupado genero el evento de transito		
                    _vVehiculo[(int)eVehiculo.eVehTra] = _vVehiculo[(int)eVehiculo.eVehIng];
                    //Limpio el vehículo ING
                    _vVehiculo[(int)eVehiculo.eVehIng] = new Vehiculo();
                    //Le asigno un numero
                    AsignarNumeroVehiculo(eVehiculo.eVehIng);
                    //Asigno TRAN a ONLINE
                    //*GetVehOnLine() = *GetVehTran();
                    //Asigno el DAC
                    //GetVehOnLine()->SetInfoDAC(m_oInfoDACOnline);

                    //Si tengo un tag en cola se lo asigno al vehiculo ingresante //TODO IMPLEMENTAR
                    //if (m_InfoTagEnCola.GetNumeroTag() != "" && CTime::GetCurrentTime() - m_TiempoLecturaTagEnCola < CTimeSpan(0, 0, DEF_TIMEOUT_TAG_ENCOLA, 0))
                    //    AsignarTagEnCola(false);
                    break;

                case eMovimiento.eSalidaSeparador:
                    _logger.Info("AdelantarVehiculo -> eSalidaSeparador");
                    //Muevo el vehículo Tra a Ant, si Ant está ocupado genero el evento de tránsito
                    EnviarEventoVehAnt();
                    _vVehiculo[(int)eVehiculo.eVehAnt] = _vVehiculo[(int)eVehiculo.eVehTra];
                    //Limpio el vehículo TRAN
                    _vVehiculo[(int)eVehiculo.eVehTra] = new Vehiculo();
                    //Asigno ANT a ONLINE
                    //*GetVehOnLine() = *GetVehAnt();
                    //m_oInfoDACOnline = GetVehAnt()->GetInfoDAC();
                    break;

                case eMovimiento.eOpCerrada:
                case eMovimiento.eOpAbortada:
                    _logger.Info("AdelantarVehiculo -> eOpAbortada/Cerrada");
                    //Asigno TRAN a ONLINE
                    _vVehiculo[(int)eVehiculo.eVehOnLine] = _vVehiculo[(int)eVehiculo.eVehTra];
                    //No hay DAC para estas operaciones
                    //GetVehOnLine()->SetInfoDAC(m_oInfoDACOnline);
                    //Muevo TRAN a ANT y genero el evento de transito.
                    //EnviarEventoVehiculo(eVehiculo.eVehTra);

                    //Borro el vehículo Tra y Ant
                    _vVehiculo[(int)eVehiculo.eVehAnt] = new Vehiculo();
                    _vVehiculo[(int)eVehiculo.eVehTra] = new Vehiculo();
                    //LimpiarVehiculos();
                    break;

                case eMovimiento.eCierreTurno:
                    //Si hay un vehículo en TRAN y/o en ANT mando los eventos
                    EnviarEventoVehAnt();

                    Vehiculo oVehiculoA = GetVehTran();

                    if (oVehiculoA.Init)
                    {
                        //Simulo una salida de separador
                        oVehiculoA.Reversa = false;
                        RegistroTransito(ref oVehiculoA);
                        AdelantarVehiculo(eMovimiento.eSalidaSeparador);
                        //Envio el evento
                        EnviarEventoVehAnt();
                    }
                    //Limpio todos los vehículos, inclusive el OnLine
                    LimpiarVehiculos(true);
                    break;
            }

            //MostrarColaVehiculos(); //TODO IMPLEMENTAR
            //UpdatePagoAdelantado(); //TODO IMPLEMENTAR
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="bClearOnLine"></param>
        private void LimpiarVehiculos(bool bClearOnLine)
        {
            //Si limpio el vehOnLine empiezo desde 0 sino desde 1
            for (int i = (bClearOnLine ? 0 : 1); i <= _vVehiculo.GetUpperBound(0); i++)
                _vVehiculo[i] = new Vehiculo();

            //if (bClearOnLine) _infoDACOnline.Clear();
        }

        /// <summary>
        /// Setea el vehículo pasado en Ant y genera el evento
        /// </summary>
        /// <param name="eVehiculo"></param>
        private void EnviarEventoVehiculo(eVehiculo eVehiculo)
        {
            Vehiculo oVehiculo = GetVehiculo(eVehiculo);
            EnviarEventoVehiculo(ref oVehiculo);
        }

        /// <summary>
        /// Genera un evento con el vehículo pVehiculo
        /// </summary>
        /// <param name="oVehiculo">Vehiculo a Enviar</param>
        private void EnviarEventoVehiculo(ref Vehiculo oVehiculo)
        {
            _logger.Info("EnviarEventoVehiculo");
            //Envio el evento ANT si está ocupado
            if (GetVehAnt().NumeroVehiculo == oVehiculo.NumeroVehiculo )
            {
                _vVehiculo[(int)eVehiculo.eVehAnt] = oVehiculo;

                EnviarEventoVehAnt();
            }
                
            else
            {
                //Asigno el vehículo eVehiculo a ANT y mando el evento
                _logger.Info("EnviarEventoVehiculo -> Asigno vehiculo a VehAnt");
                LoguearColaVehiculos();
                _vVehiculo[(int)eVehiculo.eVehAnt] = oVehiculo;
                LoguearColaVehiculos();
                //Envio el evento ANT
                EnviarEventoVehAnt();
            }
            
        }


        /// <summary>
        /// Asigna un número de vehículo al vehículo eVehiculo si no tenia
        /// uno asignado
        /// </summary>
        /// <param name="eVehiculo">Enumerado de vehiculo a asignar el número</param>
        private void AsignarNumeroVehiculo(eVehiculo eVehiculo)
        {
            //Si el vehículo no tiene número asignado le asigno uno nuevo
            if (GetVehiculo(eVehiculo).NumeroVehiculo == 0)
            {
                GetVehiculo(eVehiculo).NumeroVehiculo = GetNextNroVehiculo();
                GetVehiculo(eVehiculo).Ocupado = true;
                _logger.Info("AsignarNumeroVehiculo -> T_Vehiculo:[{Name}], NroVehiculo:[{Name}], Ocupado!!!", eVehiculo.ToString(), GetVehiculo(eVehiculo).NumeroVehiculo);
            }
        }
        /// <summary>
        /// Devuelve el próximo número de vehículo a asignar
        /// </summary>
        /// <returns>Próximo número de vehículo válido</returns>
        private ulong GetNextNroVehiculo()
        {
            return ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroVehiculo); ;
        }

        public override void SeparadorSalidaIngreso(bool esModoForzado, short RespDAC)
        {
            Stopwatch sw = new Stopwatch();
            sw.Restart();
            bool capturarVideo = true;
            char respuestaDAC = ' ';
            if (RespDAC > 0)
                respuestaDAC = Convert.ToChar(RespDAC);
            else
                respuestaDAC = 'D';

            _logger.Info("SeparadorSalidaIngreso -> Ingreso[{Name}][{Name}]", respuestaDAC, RespDAC);
            LoguearColaVehiculos();

            try
            {

                _enSeparSal = true;

                //_forzarLazo = NO_FORZAR;


                //Si el estado es pagado SetSalidaON a VehTran
                //sino a VehIng
                //Si estoy imprimiendo el ticket el estado es EVAbiertaPag pero el vehiculo no estça en VehTran
                if (GetVehTran().EstaPagado)
                    //if(m_Estado==EVAbiertaPag)
                    GetVehTran().SalidaON = true;
                else
                    GetVehIng().SalidaON = true;

                short categoVid = GetVehIng().Categoria;

                eCausaVideo accio = eCausaVideo.Violacion;

                switch (_logicaCobro.Estado)
                {
                    case eEstadoVia.EVAbiertaVenta:
                        //Pongo el semáforo de paso en Rojo
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

                        _flagIniViol = true;
                        break;
                    case eEstadoVia.EVCerrada:
                        if (!_ultimoSentidoEsOpuesto)
                            InicioViolacion();
                        else
                            capturarVideo = false;
                        break;

                    case eEstadoVia.EVAbiertaLibre:
                    case eEstadoVia.EVQuiebreBarrera:
                        InicioViolacion();
                        break;

                    case eEstadoVia.EVAbiertaCat:
                        InicioViolacion();
                        break;

                    case eEstadoVia.EVAbiertaPag:
                        LazoIngSemPaso(0, 0);
                        //m_pInterfaz->SetFormaPago(0); //TODO IMPLEMENTAR

                        categoVid = GetVehTran().Categoria;
                        accio = eCausaVideo.SeparadorEntradaOcupado;// DEF_VIDEO_ACC_SEP_INICIO;

                        break;

                    case eEstadoVia.EVModoSup:
                        InicioViolacion();
                        break;
                }


                //O:Ocupar Lazo
                //V:Ocupar Lazo sin dar pagado
                if (capturarVideo)//Solamente le da chances de comenzar a capturar si la via contraria no esta abierta
                                  //DecideCaptura(accio,categoVid,m_ControlVidTra);//Comienza/Fianliza la captura de video o no hace nada
                    DecideCaptura(accio);
                //segun la accion y la categoria del vehiculo	


                if (true /*(!m_bImprimiendoTicket)*/ )//TODO IMPLEMENTAR
                {
                    if (RespDAC < 0)
                    {
                        //LogAnomalias.Evento("CMaq_Est::OnSeparIng -> Error en PIC Lazo 1", DEF_LOG_DETALLE);
                        //msgaux.LoadString(IDS_TEXTO97); //"Error en PIC Lazo 1"
                        //Mensaje(msgaux);
                        //CEFallacri* FCEve;
                        //FCEve = new CEFallacri(GetEstacion(), m_NumTCI, &m_Fecha, CEFallacri::FCPic, GetStringPICError(msgaux, lRetDAC), GetNumTurn());
                        //m_pEventosSQL->WriteEvento(FCEve);

                        //TODO IMPLEMENTAR Generar falla Pic

                    }
                }
                /*else
                    m_ForzarLazo = FORZAR_ING;*/


                //TODO IMPLEMENTAR actualizo el Display
                //Si no estaba categorizada o no se categorizó el proximo limpio el display
                //if (m_Estado != EVAbiertaCat && !m_FlagCatego)
                //    Mensaje(DEF_MSG_CLEARDPY, DEF_MSG_DCH);


                //MostrarDatosVehiculoManual(GetVehIng()); //TODO IMPLEMENTAR

                FallaSensoresDAC();

            }

            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("****************************** SeparadorSalidaIngreso -> Salida");
            sw.Stop();
            _loggerTiempos.Info(sw.ElapsedMilliseconds);

        }
        public override bool FallaSensoresDAC(int btFiltrarSensores = 0)
        {
            bool bRetCode = false;
            //Si no está la vía en sentido opuesto
            if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
            {
                byte PICErr = 0, bEstado = 0, bValor = 0, bSensor = 0;

                byte PICErrReal = 0;
                string sMsg = "", sMsg2 = "", strAux = "", strEstado = "", strComentario = "";
                //CEvento* evepic, *evefalla;
                long lRet = 0;

                lRet = DAC_PlacaIO.Instance.ObtenerFallaSensores(ref PICErr, ref bEstado);

                if (lRet < 0)
                {
                    // No me pude comunicar
                    // TODO IMPLEMENTAR
                    //evepic = new CEFallacri(GetEstacion(), GetNumTCI(), &m_Fecha, CEFallacri::FCPic, GetStringPICError("Obtener Falla", lRet), GetNumTurn());
                    //m_pEventosSQL->WriteEvento(evepic);
                }
                else
                {
                    //Filtramos los sensores
                    PICErr = (byte)((PICErr & (~btFiltrarSensores)) & 0xff);
                    if (PICErr > 0)
                    {

                        //DAC_PlacaIO.Instance.CambiarValorEstDin(bEstado, lRet, strEstado);
                        strEstado = ((EstDinamica)bEstado).ToString();

                        DAC_PlacaIO.Instance.TraducirFallaSensoresMan(PICErr, bEstado, ref PICErrReal, ref strComentario, ref bValor);


                        bRetCode = true;
                        sMsg = GetMSGPICError(PICErr, false, ref bSensor) + ", Estado de falla: " + strEstado;

                        sMsg = "Sensor Inesperado: " + sMsg;
                        //Actualizamos el status para el Online
                        sMsg2 = GetMSGPICError(PICErrReal, true, ref bSensor);
                        //strAux.Format("CMaq_Est::FallaSensoresDAC -> %s Valor: %d %s", sMsg2, bValor, strComentario);
                        if (bSensor > 0)
                        {


                            if (bSensor == ERROR_SENSOR_SEPSAL && !DAC_PlacaIO.Instance.ExisteSensor(ConfiguracionDAC.EXISTE_SEP_SAL))
                                return false;

                            _logger.Info("Error en Sensores [{Name}]", strComentario);

                            EventoError oEventoError = new EventoError();
                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                            oEventoError.Sensor = (eSensorEvento)bSensor;
                            oEventoError.Valor = bValor;
                            oEventoError.Observacion = "";

                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, null);
                        }

                        _logger.Info("FallaSensoresDAC-> [{Name}] Valor: [{Name}] [{Name}]", sMsg2, bValor, strComentario);
                        GrabarLogSensores("FallaSensoresDAC", eLogSensores.FallaSensores);
                    }
                }
            }
            return bRetCode;
        }


        private const int ERROR_SENSOR_ALTURA = 9;
        private const int ERROR_SENSOR_BPA = 10;
        private const int ERROR_SENSOR_SEPSAL = 11;
        private const int ERROR_SENSOR_BPR = 12;
        private const int ERROR_SENSOR_SEPENT = 13;
        private const int ERROR_SENSOR_BPI = 14;


        /// <summary>
        /// Describe los errores del PIC
        /// </summary>
        /// <param name="PICErr">ID de error del PIC</param>
        /// <param name="bOnline">Indica si se debe actualizar el estado del sensor para el online</param>
        /// <param name="">Codigo de error del sensor que falló (para el evento de falla)</param>
        /// <returns></returns>
        string GetMSGPICError(byte PICErr, bool bOnline, ref byte bErrorSensor)
        {

            string strRet = "";
            string strErr = "";
            int inCantErrores = 0;

            if (((PICErr >> 0) & 0x01) > 0) //SEPARADOR ENTRADA
            {
                //if (bOnline) //TODO IMPLEMENTAR
                //    m_oEstadoSensores.SetSepEnt(DEF_SEN_FALLA);
                inCantErrores++;
                strRet += "Separador Entrada";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_SEPENT;
            }

            if (((PICErr >> 1) & 0x01) > 0)
            {
                //if (bOnline)
                //m_oEstadoSensores.SetBPR(DEF_SEN_FALLA);
                inCantErrores++;
                strRet += "Lazo de Presencia";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPR;
            }

            if (((PICErr >> 2) & 0x01) > 0)
            {
                //if (bOnline)
                //    m_oEstadoSensores.SetBPI(DEF_SEN_FALLA);
                inCantErrores++;
                strRet += "Lazo Intermedio";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPI;
            }

            if (((PICErr >> 3) & 0x01) > 0)
            {
                /*if (bOnline)
                {
                    if (GetModelo() == "D")
                        m_oEstadoSensores.SetSepSalD(DEF_SEN_FALLA);
                    else
                        m_oEstadoSensores.SetSepSalM(DEF_SEN_FALLA);
                }*/

                inCantErrores++;
                strRet += "Separador Vehicular Salida";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_SEPSAL;
            }

            if (((PICErr >> 4) & 0x01) > 0)
            {
                //if (bOnline)
                //m_oEstadoSensores.SetBPA(DEF_SEN_FALLA);
                inCantErrores++;
                strRet += "Lazo Salida Principal";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPA;
            }

            if (((PICErr >> 5) & 0x01) > 0)
            {
                //if (bOnline)
                //    m_oEstadoSensores.SetBPA2(DEF_SEN_FALLA);
                inCantErrores++;
                strRet += "Lazo Salida Auxiliar";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPA;
            }

            if (((PICErr >> 6) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Peanas";
                strRet += ", ";
                //No se manda el error
                bErrorSensor = 0;
            }

            if (((PICErr >> 7) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Sensor de Altura";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_ALTURA;
            }


            if (inCantErrores > 1)
                strErr = $"[ERRORES ({ inCantErrores}) EN PIC] - ";
            else
                strErr = "[ERROR EN PIC] - ";

            return strErr + strRet.Substring(0, strRet.Length - 2);
        }

        /// <summary>
        /// Graba en Sensoresxxx.Log el estado de los últimos N sensores.
		/// (si es un error de lógica genera una falla crítica)		
        /// </summary>
        /// <param name="causa">Descripción a loguear</param>
        /// <param name="tipo">Motivo del Logueo</param>
        void GrabarLogSensores(string causa, eLogSensores tipo = eLogSensores.FallaSensores)
        {
            try
            {
                //static CTime ctUltimoLog = 0;
                string sAux = "";
                if (tipo == eLogSensores.Logica_Sensores)
                {
                    //Error de Logica
                    _loggerSensores.Debug("Error de Logica - Causa[{Name}] - Log[{Name}]", causa, DAC_PlacaIO.Instance.GetLogSensores());
                    //Generamos falla crítica //TODO IMPLEMENTAR
                    //CEvento* eve = new CEFallacri(GetEstacion(), m_NumTCI, &m_Fecha, CEFallacri::FCPicLogica, strCausa, GetNumTurn());
                    //m_pEventosSQL->WriteEvento(eve);
                }
                else if (tipo == eLogSensores.Evento_Sensores)
                {
                    sAux = "Evento - " + causa;
                    _loggerSensores.Debug("Evento -  Causa[{Name}] - Log[{Name}]", causa, DAC_PlacaIO.Instance.GetLogSensores());
                }
                else
                {
                    //Falla de Sensores
                    //logueamos nivel detallado
                    _loggerSensores.Debug("Falla de Sensores - Causa[{Name}] - Log[{Name}]", causa, DAC_PlacaIO.Instance.GetLogSensores());
                }


            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "GrabarLogSensores Causa[{Name}] Tipo[{Name}]", causa, tipo);
            }
        }

        public override bool ViaVacia()
        {
            return false;
        }

        public override bool ViaSinVehPagados()
        {
            int i;
            bool bRet = false;      //bRet es true si está vacía

            // Se considera ocupada si hay un vehiculo Pagado
            // o se esta en una venta
            bRet = (_logicaCobro.Estado == eEstadoVia.EVAbiertaPag || _logicaCobro.Estado == eEstadoVia.EVAbiertaVenta);

            return bRet;

        }

        /// <summary>
        /// Limpia los dialogos que estan abiertos y pasael semaforo a rojo
        /// </summary>
        /// <param name="sobligado"></param>
        /// <param name="lobligado"></param>
        /// <returns></returns>
        private void InicioViolacion()
        {
            try
            {
                //Pongo el semáforo de paso en Rojo
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

                _flagIniViol = true;

            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

        }

        private void RegistroTransito(ref Vehiculo oVehiculo)
        {

            _logger.Info( $"RegistroTransito -> Inicio NroVeh [{oVehiculo.NumeroVehiculo}]" );
            DateTime T1 = DateTime.Now; //TODO IMPLEMENTAR m_Fecha

            EnviarEventoVehAnt();

            oVehiculo.Operacion = "TR";

            //Sino tiene número de transito le asigno uno
            if (oVehiculo.NumeroTransito == 0)
            {
                oVehiculo.NumeroTransito = IncrementoTransito();
            }
            //Sino tiene fecha de transito le asigno una
            if (oVehiculo.Fecha == DateTime.MinValue)
            {
                oVehiculo.Fecha = T1;
            }
            if (!oVehiculo.InfoDac.YaTieneDAC)
            {
                CategoriaPic(ref oVehiculo, T1, false, null);
            }

            _huellaDAC = string.Empty;

            if (oVehiculo.InfoDac.Categoria > 0)
            {
                TarifaABuscar oTarifaABuscar = new TarifaABuscar();



                oTarifaABuscar.Catego = oVehiculo.InfoDac.Categoria;
                oTarifaABuscar.Estacion = ModuloBaseDatos.Instance.ConfigVia.NumeroDeEstacion;
                oTarifaABuscar.TipoTarifa = oVehiculo.TipoTarifa;
                oTarifaABuscar.FechAct = T1;
                oTarifaABuscar.FechaComparacion = T1;
                oTarifaABuscar.FechVal = T1;
                oTarifaABuscar.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido;

                Tarifa oTarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar);
                oVehiculo.InfoDac.Tarifa = oTarifa.Valor;

                //ModuloBaseDatos.Instance.BuscarTarifaAsync(oTarifaABuscar);

                if (oVehiculo.InfoDac.Categoria != oVehiculo.Categoria)
                {
                    if (oTarifa.Valor > oVehiculo.Tarifa)
                    {
                        DecideAlmacenar(eAlmacenaMedio.DiscrepanciaEncontra, ref oVehiculo);

                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Disc);
                    }
                    else
                    {
                        DecideAlmacenar(eAlmacenaMedio.DiscrepanciaFavor, ref oVehiculo);

                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.DiscF);
                    }

                }

            }
            else
            {
                if (oVehiculo.InfoDac.Categoria == -1)
                {
                    if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.FLazo);
                }
                else if (oVehiculo.InfoDac.Categoria == -2)
                {
                    if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.FSensor);
                }
                DecideAlmacenar(eAlmacenaMedio.FallaCategoria, ref oVehiculo);
            }
            if (oVehiculo.FallaSalida || oVehiculo.SalioChupado)
            {
                DecideAlmacenar(eAlmacenaMedio.DiscrepanciaEncontra, ref oVehiculo);
            }

            switch (oVehiculo.FormaPago)
            {
                case eFormaPago.CTExen:
                    DecideAlmacenar(eAlmacenaMedio.Exento, ref oVehiculo);
                    break;
                case eFormaPago.CTTExen:
                case eFormaPago.CTChExen:
                    DecideAlmacenar(eAlmacenaMedio.TagExento, ref oVehiculo);
                    break;
                //case eFormaPago.CTTAbono:
                //case eFormaPago.CTChAbono:
                //    DecideAlmacenar(eAlmacenaMedio., ref oVehiculo);
                //    break;
                case eFormaPago.CTTCC:
                case eFormaPago.CTChCC:
                    DecideAlmacenar(eAlmacenaMedio.TagPospago, ref oVehiculo);
                    break;
                case eFormaPago.CTTPrepago:
                case eFormaPago.CTChPrepago:
                    DecideAlmacenar(eAlmacenaMedio.TagPrepago, ref oVehiculo);
                    break;
                case eFormaPago.CTTUFRE:
                case eFormaPago.CTChUFRE:
                    DecideAlmacenar(eAlmacenaMedio.TagUfre, ref oVehiculo);
                    break;
                case eFormaPago.CTPagoDiferido:
                    DecideAlmacenar(eAlmacenaMedio.PagoDiferido, ref oVehiculo);
                    break;

            }

            switch (oVehiculo.FormaPago)
            {
                case eFormaPago.CTTExen:
                case eFormaPago.CTTAbono:
                case eFormaPago.CTTOmnibus:
                case eFormaPago.CTTCC:
                case eFormaPago.CTTTransporte:
                case eFormaPago.CTTPrepago:
                case eFormaPago.CTTUFRE:
                case eFormaPago.CTTFederado:
                    if (oVehiculo.InfoTag.LecturaManual == 'S')
                        DecideAlmacenar(eAlmacenaMedio.Manual, ref oVehiculo);
                    break;
            }

            DecideAlmacenar(eAlmacenaMedio.Categoria, ref oVehiculo);

            if (oVehiculo.CodigoObservacion > 0)
                DecideAlmacenar(eAlmacenaMedio.Observado, ref oVehiculo);

            ModuloBaseDatos.Instance.AlmacenarTransitoTurno(oVehiculo, _logicaCobro.GetTurno);

            _logger.Info("RegistroTransito -> Fin");
        }

        /// <summary>
        /// Metodo que decide almacenar (mover) los videos en base a configuracion de base
        /// </summary>
        /// <param name="causa"></param>
        /// <param name="oVehiculo"></param>
        public override void DecideAlmacenar(eAlmacenaMedio causa, ref Vehiculo oVehiculo)
        {
            string almacena = "";
            int porcentajeMuestra = 0;
            bool foundEve = false, foundCat = false;

            try
            {
                // Chequeo si ya se almacenaron videos/fotos de este vehiculo
                bool fotoVideoSinAlmacenar = false;
                foreach (InfoMedios infoM in oVehiculo.ListaInfoFoto)
                {
                    if (!infoM.Almacenar)
                        fotoVideoSinAlmacenar = true;
                }
                foreach (InfoMedios infoM in oVehiculo.ListaInfoVideo)
                {
                    if (!infoM.Almacenar)
                        fotoVideoSinAlmacenar = true;
                }
                // Si ya se almacenaron todos, no chequeo condiciones para almacenar
                if (!fotoVideoSinAlmacenar)
                    return;

                // Consulto por porcentaje de almacenamiento por tipo de evento
                List<VideoEve> listaVideoEve = ModuloBaseDatos.Instance.BuscarVideoEve(causa.GetDescription());

                if (listaVideoEve?.Count > 0)
                {
                    foundEve = true;
                    almacena = listaVideoEve[0].Almacena;
                }
                else
                {
                    _logger.Debug("No se encontro la causa en la base de datos ");
                }

                if (foundEve) //Defino porcentaje de almacenamiento segun dato de la consulta
                {
                    switch (almacena[0])
                    {
                        case 'S':
                            porcentajeMuestra = 100;
                            break;
                        case 'N':
                            porcentajeMuestra = 0;
                            break;
                        case 'M':
                            porcentajeMuestra = listaVideoEve[0].PorcentajeMuestra.GetValueOrDefault();
                            break;
                        default:
                            porcentajeMuestra = 100;
                            break;
                    }
                }

                List<VideoCat> listaVideoCat = ModuloBaseDatos.Instance.BuscarVideoCat(oVehiculo.Categoria);
                if (listaVideoCat?.Count >= 1)
                {
                    foundCat = true;
                    almacena = listaVideoCat[0].Almacena;
                }
                else
                {
                    _logger.Debug("No se encontro la categoria en la base de datos ");
                }

                if (foundCat) //Defino porcentaje de almacenamiento segun dato de la consulta
                {

                    switch (almacena[0])
                    {
                        case 'S':
                            porcentajeMuestra = 100;
                            break;
                        case 'N':
                            break;
                        case 'M':
                            // Me quedo con el maximo de almacenamiento entre tipo de evento y categoria
                            porcentajeMuestra = Math.Max(porcentajeMuestra, listaVideoCat[0].PorcentajeMuestra.GetValueOrDefault());
                            break;
                        default:
                            porcentajeMuestra = 100;
                            break;
                    }
                }
                else
                {
                    porcentajeMuestra = 100;
                }


                Random rnd = new Random();
                int random = rnd.Next(0, 100);

                if (porcentajeMuestra > 0 && porcentajeMuestra >= random)
                {
                    ModuloVideo.Instance.AlmacenarVideo(ref oVehiculo);
                    ModuloFoto.Instance.AlmacenarFoto(ref oVehiculo);
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        public override void SetLogicaCobro(ILogicaCobro logicaCobro)
        {
            _logicaCobro = logicaCobro;
        }

        public override void SetVehiculo(Vehiculo[] aVehiculo)
        {

        }
        /// <summary>
        /// Incremento el número de transito y lo devuelvo.
        /// </summary>
        /// <returns></returns>
        private ulong IncrementoTransito()
        {
            //return _numeroTransito++;
            return ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
        }

        public override void ProcesarLecturaTag(eEstadoAntena estado, Tag tag, eTipoLecturaTag tipoLectura, TagBD tagManualOnline = null, Vehiculo oVehOpcional = null)
        {
            bool bUsarDatosVehiculo = false;
            Vehiculo oVehiculo = null;
            TagBD tagBD = new TagBD();
            TagBD tagBDOnline = new TagBD();
            eErrorTag errorTag;
            try
            {
                if (tag != null)
                {
                    _logger.Info("ProcesarLecturaTag -> Inicio Tag[{Name}] TID[{Name}] tipoLectura[{Name}]", tag.NumeroTag, tag.NumeroTID, tipoLectura.ToString());

                    //Indico que estoy procesando un tag, si hago un return lo paso a false
                    _tengoTagEnProceso = true;

                    DateTime dtTiempoLectura = tag.HoraLectura;
                    InfoTag oInfoTag = new InfoTag(tag);

                    //De acuerdo al tipo de lectura de tag, hago cosas particulares
                    if (tipoLectura == eTipoLecturaTag.Manual)
                    {
                        //utilizamos el mismo vehiculo al que se le agregó la recarga
                        if (oVehOpcional != null && oVehOpcional.ListaRecarga.Any(x => x != null))
                        {
                            oInfoTag.LecturaManual = oVehOpcional.LecturaManual;
                            oVehiculo = oVehOpcional;
                        }
                        else
                        {
                            oInfoTag.LecturaManual = 'S';
                            oVehiculo = GetVehIng();
                        }
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'O';
                    }
                    if (tipoLectura == eTipoLecturaTag.OCR || tipoLectura == eTipoLecturaTag.Manual)
                    {
                        oInfoTag.LecturaManual = 'O';
                        oVehiculo = GetVehIng();
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'O'; //GAB: Confirmar si está bien que cambie TipOp por ser OCR
                        //oInfoTag.TipOp = 'T';
                    }

                    oVehiculo.LecturaManual = oInfoTag.LecturaManual;

                    //Valido si el tag no se leyo hace poco tiempo o esta siendo procesado. Internamente busca en la base de datos.
                    // eErrorTag errorTag = ValidarTag(ref oInfoTag, ref oVehiculo, ref oVehiculoA, false, bUsarDatosVehiculo);
                    //if ((errorTag == eErrorTag.NoError && !oVehiculo.ListaRecarga.Any(x => x != null)) || (oVehiculo.ListaRecarga.Any(x => x != null) && oVehiculo.InfoTag.NumeroTag != oInfoTag.NumeroTag))

                    if (tagManualOnline == null)
                        tagBDOnline = ModuloBaseDatos.Instance.ObtenerTagEnLinea(oInfoTag.NumeroTag.Trim(), "O", oInfoTag.TipOp.ToString());
                    else
                        tagBDOnline = tagManualOnline;

                    if (tagBD.Estado == 103)
                    {
                        ModuloPantalla.Instance.LimpiarMensajes();
                        GetPrimeroSegundoVehiculo().Patente = tagBD.NumeroTag;
                        ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "La cuenta esta vencida");
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(GetPrimeroSegundoVehiculo(), ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);
                        return;
                    }

                    oInfoTag.OrigenSaldo = 'O';

                    //Continuamos con la validacion local
                    if (tagBDOnline?.EstadoConsulta != EnmStatusBD.OK && tagBDOnline?.EstadoConsulta != EnmStatusBD.SINRESULTADO /*&& tagManualOnline == null*/)
                        errorTag = eErrorTag.Desconocido;
                    else
                        errorTag = ValidarTagBaseDatos(ref oInfoTag, ref oVehiculo, bUsarDatosVehiculo, ref tagBDOnline);

                    // Si falla la consulta online
                    if (errorTag == eErrorTag.Desconocido && tagManualOnline == null)
                    {
                        //Busco el tag en la base de datos local
                        tagBD = ModuloBaseDatos.Instance.BuscarTagPorNumero(oInfoTag.NumeroTag);

                        _logger.Info("Ejecuto consulta en linea TAG[{name}] -> Resultado [{name}], salgo de ProcesarLecturaTag", tag.NumeroTag, tagBD.EstadoConsulta.ToString());

                        //No valido si no encontré nada en la consulta
                        if (tagBD.EstadoConsulta == EnmStatusBD.OK)
                            errorTag = ValidarTagBaseDatos(ref oInfoTag, ref oVehiculo, bUsarDatosVehiculo, ref tagBD);

                        oInfoTag.OrigenSaldo = 'O';
                    }
                    else
                        tagBD = tagBDOnline;

                    _logger.Info($"TAG[{tag.NumeroTag}] - PATENTE[{tagBD.Patente}]");

                    //Al setear el error, se verifica si el tag esta habilitado o no.
                    oInfoTag.ErrorTag = errorTag;
                    oInfoTag.FechaPago = DateTime.Now;
                    _logger.Info("ProcesarLecturaTag[{Name}]", oInfoTag.ErrorTag.GetDescription());
                    if (oInfoTag.TipBo == '\0' && oInfoTag.SaldoInicial > 0)
                        oInfoTag.TipBo = 'P';
                    oVehiculo.InfoTag = oInfoTag;


                    //Asigno el tag al vehículo, y es un tag diferente o la causa es diferente (y no es repetido)
                    /* if (tipoLectura == eTipoLecturaTag.Manual ||
                         (((oVehiculo.InfoTag.Patente != oInfoTag.Patente) && (oInfoTag.ErrorTag != eErrorTag.Repetido)) ||
                           oVehiculo.InfoTag.RecargaReciente == 'S'))*/
                    {

                        //Si es tag manual o T Chip se asigna al primer vehiculo
                        //sino al que está en la cola
                        if (tipoLectura == eTipoLecturaTag.OCR)
                        {
                            AsignarTagLeido(oInfoTag, true, true);
                        }
                        else
                        {
                            bool manualOChip = tipoLectura == eTipoLecturaTag.Manual;

                            if (oVehiculo.InfoTag.NumeroTag == oInfoTag.NumeroTag && oVehiculo.ListaRecarga.Any(x => x != null))
                                AsignarTagLeido(oInfoTag, manualOChip, manualOChip, oVehiculo.NumeroVehiculo);
                            else
                                AsignarTagLeido(oInfoTag, manualOChip, manualOChip);
                        }
                    }
                }
            }

            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
                _tengoTagEnProceso = false;
            }

            LoguearColaVehiculos();
        }

        private bool AsignarTagLeido(InfoTag oInfoTag, bool bLecturaManual, bool bNoAsignarAVehiculo, ulong ulNumVeh = 0)
        {
            bool bRet = false, bEnc = false;
            int i;
            ulong ulNroVehiculo = 0;
            Vehiculo oVeh = null;

                _logger.Info("AsignarTagLeido -> Inicio. Tag:[{Name}] LecturaManual:[{Name}] NoAsignarAVehiculo:[{Name}] TipOp[{Name}]", oInfoTag.NumeroTag, bLecturaManual, bNoAsignarAVehiculo, oInfoTag.TipOp);

                oVeh = GetVehIng();

                //Si el tag está habilitado lo tengo que sumar en el turno, si se encola ya está descontado el saldo
                if (oInfoTag.GetTagHabilitado() )
                {
                    RegistroPagoTag(ref oInfoTag, ref oVeh);
                }

                bRet = true;
                //Asigno la fecha de pago con tag
                oInfoTag.FechaPago = DateTime.Now;

                if (bLecturaManual)
                {
                    if (ulNumVeh > 0)
                        oVeh = GetVehiculo(BuscarVehiculo(ulNumVeh));
                    else
                    {
                        oVeh = GetVehIng();
                    }

                    if (oVeh.NumeroVehiculo == 0)
                    {
                    //Cuando no tiene numero es porque es P1 con la via vacia
                        GetVehIng().NumeroVehiculo = GetNextNroVehiculo();
                        oVeh = GetVehIng();
                    }

                    //Asigno el tag al vehículo Ing
                    _logger.Info("AsignarTagLeido -> Asigno tag Tag:[{Name}], Vehículo:[{Name}]", oInfoTag.NumeroTag, oVeh.NumeroVehiculo);

                    AsignarTagAVehiculo(oInfoTag, oVeh.NumeroVehiculo);
                    //Si el tag está habilitado lo tengo que sumar en el turno

                }
                else
                {
                    _infoTagLeido.SetInfoTag(oInfoTag);
                    bEnc = false;
                    //Si me pidieron no asignarlo no lo asigno a nadie
                    if (!bNoAsignarAVehiculo)
                    {
                        _logger.Info("AsignarTagLeido -> Buscamos Nro [{Name}] Cant [{Name}] TipOp[{Name}]", _infoTagLeido.GetNumeroVehiculo(0), _infoTagLeido.GetCantVehiculos(), _infoTagLeido.GetInfoTag().TipOp);

                        ulNroVehiculo = _infoTagLeido.GetNumeroVehiculo(0);
                        //Si tiene un solo vehiculo y todavia existe lo asigno al vehiculo
                        if (_infoTagLeido.GetCantVehiculos() == 1)
                        {
                            //Busco el vehiculo
                            i = (int)eVehiculo.eVehP3;
                            while (!bEnc && i <= (int)eVehiculo.eVehC0)
                            {
                                oVeh = GetVehiculo((eVehiculo)i);
                                if (oVeh.NumeroVehiculo == ulNroVehiculo)
                                {
                                    //UFRE y Federado no están pagados pero ya tienen tag habilitado
                                    //Sino le pisaba el tag UFRE si se leia otro antes de cobrar
                                    if (!oVeh.EstaPagado && !oVeh.CobroEnCurso && !oVeh.InfoTag.GetTagHabilitado() && !oVeh.NoPermiteTag
                                        && !oVeh.ProcesandoViolacion)
                                    {
                                        //Solo lo asigno si no está pagado
                                        bEnc = true;
                                    }
                                    else
                                    {
                                        _logger.Info("AsignarTagLeido -> Vehiculo estaba pagado [{Name}] [{Name}] [{Name}]", oVeh.FormaPago, oVeh.TipOp, oVeh.TipBo);
                                    }
                                }
                                i++;
                            }
                        }
                    }
                    else
                    {
                        //Ya me apagaron la antena
                        //lo dejo pendiente sin ningun vehiculo
                        //para asignarlo al proximo que entra
                        bEnc = false;
                        _infoTagLeido.BorrarListaVehiculos();
                    }

                    if (bEnc)
                    {
                        _logger.Info("AsignarTagLeido -> Asigno tag Tag:[{Name}], Vehículo:[{Name}] Tipop[{Name}]", oInfoTag.NumeroTag, _infoTagLeido.GetNumeroVehiculo(0), oInfoTag.TipOp);

                        //Hay un solo vehículo, llamo a AsignarTagAVehiculo
                        AsignarTagAVehiculo(oInfoTag, ulNroVehiculo);
                    }

                    //Ya el tag pasó a algun lado, limpiamos el dato del tag
                    //Si esta habilirado ya no asignamos más a estos vehiculos
                    if (oInfoTag.GetTagHabilitado())
                    {
                        _infoTagLeido.Clear();
                    }
                }
                _loggerTransitos?.Info($"P;{oVeh.Fecha.ToString("HH:mm:ss.ff")};{oVeh.Categoria};{oVeh.TipOp};{oVeh.TipBo};{oVeh.GetSubFormaPago()};{oVeh.Tarifa};{oVeh.NumeroTicketF};{oVeh.Patente};{oInfoTag.NumeroTag};{oInfoTag.Ruc};{0};{oVeh.NumeroVehiculo};{oVeh.NumeroTransito}", bLecturaManual == false ? "0" : "1");

                _logger.Info("AsignarTagLeido -> Fin");
            
            return bRet;
        }

        void RegistroPagoTag(ref InfoTag oInfoTag, ref Vehiculo oVeh)
        {
            _logger.Info($"RegistroPagoTag Inicio - NroVehiculo[{oVeh.NumeroVehiculo}] Tag Prepago[{oInfoTag.EsPrepago()}]");

            //Descontamos el viaje
            if (oInfoTag.EsPrepago())
                oInfoTag.DescontarSaldo();
        }


        private bool AsignarTagAVehiculo(InfoTag oInfoTag, ulong ulNroVehiculo)
        {
            bool bEnc = false, bRet = false;
            int i = 0;
            DateTime dtFechVal = DateTime.MinValue;

            bool bAsignandoTagAVehiculo = true;
            eVehiculo vehiculo = eVehiculo.eVehP1;
            Vehiculo oVeh = null, oVehAux = null, VehIng = null;
            _logger.Info("AsignarTagAVehiculo -> Inicio Tag:[{Name}] Vehiculo:[{Name}] TipOp:[{Name}]", oInfoTag.NumeroTag, ulNroVehiculo, oInfoTag.TipOp);
            VehIng = GetVehIng();

            if (ulNroVehiculo > 0)
            {
                oVeh = GetVehIng();
                if (oVeh.NumeroVehiculo == ulNroVehiculo)
                    bEnc = true;
            }
            if (VehIng.ListaRecarga.Any(x => x != null)) // Si tiene una recarga
            {
                oVeh = GetVehiculo(vehiculo);
                bEnc = true;
            }

            if (bEnc)
            {
                char multiplesTags = !string.IsNullOrEmpty(oVeh.InfoTag.NumeroTag) && (oVeh.InfoTag.NumeroTag != oInfoTag.NumeroTag) ? '1' : '0';
                bool bReemplazar = true;

                _logger.Info("AsignarTagAVehiculo -> Encontramos el vehiculo, tiene multiples Tags? [{0}], LecturaManual? [{1}], "
                             , multiplesTags == '1' ? "SI" : "NO",
                             oInfoTag.LecturaManual == 'S' ? "SI" : "NO"
                             );


                if (bReemplazar)
                {
                    oVehAux = new Vehiculo();
                    oVehAux.InfoTag = oInfoTag;

                    short causa = -1;

                    if (_logicaCobro.ModoQuiebre != eQuiebre.Nada)
                        causa = 1;
                    //else if( _logicaCobro.EnComitiva )
                    //    causa = 2;
                    else if (oVehAux.EstaPagado)
                        causa = 3;

                    if (oInfoTag.LecturaManual == 'N' && (multiplesTags == '1' || causa > 0))
                    {
                        // Evento de tag sin vehiculo
                        ModuloEventos.Instance.SetVehiculoSinTag(_logicaCobro.GetTurno, oVehAux, causa, multiplesTags);
                    }

                    //Intento quitar el elemento de la lista
                    //(no va a estar si lei en un caso con un solo vehiculo)
                    //Quito el elemento de la lista que tiene como número de tag el de oInfoTag
                    //bRet = DepartTagLeido(oInfoTag.NumeroTag);

                    _logger.Info("AsignarTagAVehiculo -> Tag:[{Name}] Vehículo:[{Name}] TipOp[{Name}]", oInfoTag.NumeroTag, ulNroVehiculo, oInfoTag.TipOp);

                    //revisar si el vehiculo sigue con el nro
                    if (oVeh.InfoTag.Patente != "")
                    {
                        if (oVeh.NumeroVehiculo != ulNroVehiculo)
                            ulNroVehiculo = 0;
                    }

                    //revisamos que no este en una violacion para asignar el tag
                    if (!oVeh.ProcesandoViolacion)
                    { 
                        bool bForzarMuestra = false;

                        {
                            _logger.Info("AsignarTagAVehiculo -> Cumple condición T_Vehiculo:[{Name}]", ((eVehiculo)vehiculo).ToString());

                            //Si es un tag habilitado levanto la barrera
                            if (oInfoTag.GetTagHabilitado() && oInfoTag.ErrorTag != eErrorTag.Vencido)
                            {
                                _logger.Info("AsignarTagAVehiculo -> Tag habilitado, subo barrera? T_Vehiculo:[{Name}]", (oVeh.ToString()));

                                if (oInfoTag.TipoTag != eTipoCuentaTag.Ufre && oInfoTag.TipOp != 'C' )
                                {
                                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                                    _logger.Info("AsignarTagAVehiculo -> BARRERA ARRIBA!!");
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

                                    InfoPagado oPagado = new InfoPagado();

                                    
                                    oPagado.Tarifa = oInfoTag.Tarifa;
                                    if(oInfoTag.TipOp == 'P')
                                        oPagado.TipOp = 'T';
                                    else
                                        oPagado.TipOp = 'T';
                                    oPagado.Categoria = oVeh.Categoria;
                                    oPagado.TipBo = oInfoTag.TipBo;
                                    if (oInfoTag.TipoTag == eTipoCuentaTag.Exento)
                                        oPagado.FormaPago = eFormaPago.CTTExen;
                                    else if (oInfoTag.TipoTag == eTipoCuentaTag.Prepago)
                                        oPagado.FormaPago = eFormaPago.CTTPrepago;
                                    oPagado.Fecha = DateTime.Now;
                                    oPagado.FechaFiscal = oPagado.Fecha;
                                    oPagado.TipoTarifa = oInfoTag.TipoTarifa;
                                    oPagado.Patente = oInfoTag.Patente;
                                    oPagado.TipoDiaHora = oInfoTag.TipDH;
                                    if (oVeh.InfoOCRDelantero.Patente != "")
                                        oPagado.InfoOCRDelantero = oVeh.InfoOCRDelantero;
                                    oVeh.InfoCliente.Activo = true;
                                    oVeh.InfoCliente.Clave = oInfoTag.NroCliente;
                                    oVeh.InfoCliente.Ruc = oInfoTag.Ruc;
                                    oVeh.InfoCliente.RazonSocial = oInfoTag.NombreCuenta;
                                    oVeh.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                                    oVeh.NumeroBoleto = oPagado.Patente;
                                    if(oInfoTag.TipoDocumento == 1)
                                        oPagado.NumeroFactura = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroFactura);
                                    else if(oInfoTag.TipBo != 'X')
                                        oPagado.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);
                                    if (oVeh.Categoria == 0)
                                    {    oVeh.Categoria = oInfoTag.Categoria;
                                         oVeh.CategoDescripcionLarga = oInfoTag.CategoDescripcionLarga;
                                    }
                                    TarifaABuscar oTarifaABuscar= new TarifaABuscar();
                                    oTarifaABuscar.Catego = oInfoTag.Categoria;
                                    oTarifaABuscar.Estacion = ModuloBaseDatos.Instance.ConfigVia.NumeroDeEstacion;
                                    oTarifaABuscar.TipoTarifa = oInfoTag.TipoTarifa;
                                    oTarifaABuscar.FechAct = DateTime.Now;
                                    oTarifaABuscar.FechaComparacion = DateTime.Now;
                                    oTarifaABuscar.FechVal = DateTime.Now;
                                    oTarifaABuscar.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido;
                                    Tarifa tarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar);

                                    oPagado.Categoria = oInfoTag.Categoria;
                                    oPagado.CategoriaDesc = tarifa.DescripcionLarga;
                                    oPagado.Tarifa = oInfoTag.Tarifa;

                                    if(oInfoTag.AfectaDetraccion == 'S')
                                    {
                                        oPagado.AfectaDetraccion = 'S';
                                        oPagado.ValorDetraccion = oInfoTag.ValorDetraccion;
                                        oVeh.EstadoDetraccion = 3;

                                        EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

                                        if(errorImpresora == EnmErrorImpresora.SinFalla)
                                            ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Efectivo,oVeh);
                                    }

                                    oVeh.CargarDatosPago(oPagado);
                                    oVeh.Operacion = "CB";
                                    
                                    
                                    GetVehOnline().CategoriaProximo = 0;
                                    GetVehOnline().InfoDac.Categoria = 0;

                                    //_logicaCobro.Estado = eEstadoVia.EVAbiertaPag;
                                    // Adelantar Vehiculo
                                    AdelantarVehiculo(eMovimiento.eOpPago);

                                    ModuloBaseDatos.Instance.AlmacenarPagadoTurno(oVeh, _logicaCobro.GetTurno);

                                    if(oPagado.FormaPago == eFormaPago.CTExen)
                                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Franquicia);
                                    oVeh = GetVehTran();
                                    oVeh.InfoTag = oInfoTag;                                   
                                    _logicaCobro.Estado = eEstadoVia.EVAbiertaPag;
                                    ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno,oVeh.InfoCliente, oVeh);
                                    //Se envía setCobro
                                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, oVeh);
                                    List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                                }
                                Mimicos mimicos = new Mimicos();
                                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                                ClassUtiles.InsertarDatoVia(oVeh, ref listaDatosVia);
                                if(oInfoTag.TipoTarifa != 0)
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Tarifa con Descuento");
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, oInfoTag.Mensaje);                               
                                if (oInfoTag.PagoEnVia != 'S' && oVeh.TipBo != 'X')
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Saldo: {ClassUtiles.FormatearMonedaAString(oInfoTag.SaldoFinal)}");
                                    List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);
                                }                                    
                                else if (oInfoTag.PagoEnVia == 'S')
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Cobrar con la tecla: Diferenciados");
                                    List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                                    oVeh.InfoTag = oInfoTag;
                                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia3);
                                }
                                    
                            }
                            else
                            {
                                _logger.Info("AsignarTagAVehiculo -> Tag no habilitado T_Vehiculo:[{Name}]", ((eVehiculo)vehiculo).ToString());
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, oInfoTag.Mensaje);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Saldo: {ClassUtiles.FormatearMonedaAString(oInfoTag.SaldoFinal)}");
                                GetVehIng().Patente = oInfoTag.Patente;
                            }

                            bForzarMuestra = true;
                        }

                    }

                }
            }
            else
            {
                //No encontré el vehiculo, por las dudas compruebo que no este asignado a otro vehiculo
                if (ulNroVehiculo > 0)
                {
                    i = (int)eVehiculo.eVehP3;

                    while (!bEnc && i <= (int)eVehiculo.eVehC0)
                    {
                        oVeh = GetVehiculo((eVehiculo)i);

                        if (oVeh.NumeroVehiculo == ulNroVehiculo && oVeh.EstaPagado)
                            //Solo lo asigno si no está pagado
                            bEnc = true;
                        i++;
                    }
                    if (bEnc)
                    {
                        //Encontré el vehiculo con ese tag ya pagado
                        _logger.Info("AsignarTagAVehiculo -> Tag [{0}] ya esta asignado y pagado a vehiculo Nro [{1}] [{2}]", oInfoTag.NumeroTag, ulNroVehiculo, ((eVehiculo)(i - 1)).ToString());
                        //Lo saco de la lista
                        //bRet = DepartTagLeido(oInfoTag.NumeroTag);
                    }
                }
            }

            bAsignandoTagAVehiculo = false;
            _logger.Info("AsignarTagAVehiculo -> Fin");
            //MostrarColaVehiculos();
            //UpdatePagoAdelantado();
            return bRet;
        }

        /// <summary>
        /// Cada comando recibido del servicio de video se procesa acá.
        /// </summary>
        /// <param name="estado"></param>
        /// <param name="oVideo"></param>
        private void ProcesarComandoVideo(eEstadoVideo estado, byte numeroSensor, Video oVideo, Vehiculo vehiculo)
        {

        }

        /// <summary>
        /// Cada comando recibido del servicio de video se procesa acá.
        /// </summary>
        /// <param name="estado"></param>
        /// <param name="oVideo"></param>
        private void ProcesarComandoFoto(eEstadoFoto estado, byte numeroSensor, Foto oFoto, Vehiculo vehiculo)
        {

        }

        override public void OpCerradaEvento(short origenTecla, ref Vehiculo oVehiculo)
        {
            try
            {
                DateTime FechaCerrada;
                string sOper = "";
                bool bApagada = false;

                if (origenTecla == 1)
                {
                    //Si no tenia fecha
                    if (oVehiculo.Fecha == DateTime.MinValue)
                    {
                        //Asigno la fecha del evento solo si no es por reinicio
                        //Cerrada
                        oVehiculo.Fecha = DateTime.Now; //m_Fecha
                    }
                    FechaCerrada = oVehiculo.Fecha;

                    sOper = "CE";
                    bApagada = false;
                }
                else
                {
                    //Reinicio
                    //la fecha del evento es de cuando se cobró el vehículo
                    FechaCerrada = oVehiculo.Fecha;
                    sOper = "RE";
                    bApagada = true;
                }

                oVehiculo.Operacion = sOper;
                oVehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);

                //Enviamos la ultima huella
                oVehiculo.Huella = _huellaDAC;
                
                //Limpio la ultima huella leida
                _huellaDAC = string.Empty;

                DecideAlmacenar(eAlmacenaMedio.SIP, ref oVehiculo);

                switch (oVehiculo.FormaPago)
                {
                    case eFormaPago.CTExen:
                        DecideAlmacenar(eAlmacenaMedio.Exento, ref oVehiculo);
                        break;
                    case eFormaPago.CTTExen:
                    case eFormaPago.CTChExen:
                        DecideAlmacenar(eAlmacenaMedio.TagExento, ref oVehiculo);
                        break;
                    case eFormaPago.CTTCC:
                    case eFormaPago.CTChCC:
                        DecideAlmacenar(eAlmacenaMedio.TagPospago, ref oVehiculo);
                        break;
                    case eFormaPago.CTTPrepago:
                    case eFormaPago.CTChPrepago:
                        DecideAlmacenar(eAlmacenaMedio.TagPrepago, ref oVehiculo);
                        break;
                    case eFormaPago.CTTUFRE:
                    case eFormaPago.CTChUFRE:
                        DecideAlmacenar(eAlmacenaMedio.TagUfre, ref oVehiculo);
                        break;
                    case eFormaPago.CTPagoDiferido:
                        DecideAlmacenar(eAlmacenaMedio.PagoDiferido, ref oVehiculo);
                        break;

                }

                switch (oVehiculo.FormaPago)
                {
                    case eFormaPago.CTTExen:
                    case eFormaPago.CTTAbono:
                    case eFormaPago.CTTOmnibus:
                    case eFormaPago.CTTCC:
                    case eFormaPago.CTTTransporte:
                    case eFormaPago.CTTPrepago:
                    case eFormaPago.CTTUFRE:
                    case eFormaPago.CTTFederado:
                        if (oVehiculo.InfoTag.LecturaManual == 'S')
                            DecideAlmacenar(eAlmacenaMedio.Manual, ref oVehiculo);
                        break;
                }


                //Analizo Sin Tomar en cuenta el evento
                //" ":Solo Categoria
                DecideAlmacenar(eAlmacenaMedio.Categoria, ref oVehiculo);

                if (oVehiculo.CodigoObservacion > 0)
                    DecideAlmacenar(eAlmacenaMedio.Observado, ref oVehiculo);



                if (bApagada)
                {
                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Apagado);
                }
                else
                {
                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Cerr);
                }




                _vVehiculo[(int)eVehiculo.eVehOnLine] = oVehiculo;
                EnviarEventoVehiculo(ref oVehiculo);


            }

            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        /// <summary>
        /// Carga los datos en el vehículo pVehiculo de la operación abortada
        /// </summary>
        /// <param name="oVehiculo">vehículo donde se guardan los datos de la op. abortada</param>
        override public void OpAbortadaEvento(ref Vehiculo oVehiculo)
        {
            string nada;

            try
            {
                _logger.Info("OpAbortadaEvento->OpAbortada, Se ejecuta");
                //Si no tenia fecha
                if (oVehiculo.Fecha == DateTime.MinValue)
                    //Fecha del evento
                    oVehiculo.Fecha = DateTime.Now;//(m_Fecha);

                //Asigno el tipo de operación y el número de transito
                oVehiculo.Operacion = "AB";

                oVehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);

                //Enviamos la ultima huella
                oVehiculo.Huella = _huellaDAC;
                //Limpio la ultima huella leida
                _huellaDAC = string.Empty;

                //m_EstadoAbort = 'S';
                nada = "Tránsito Anulado"; //Operacion Abortada

                DecideAlmacenar(eAlmacenaMedio.Cancelada, ref oVehiculo);

                switch (oVehiculo.FormaPago)
                {
                    case eFormaPago.CTExen:
                        DecideAlmacenar(eAlmacenaMedio.Exento, ref oVehiculo);
                        break;
                    case eFormaPago.CTTExen:
                    case eFormaPago.CTChExen:
                        DecideAlmacenar(eAlmacenaMedio.TagExento, ref oVehiculo);
                        break;
                    case eFormaPago.CTTCC:
                    case eFormaPago.CTChCC:
                        DecideAlmacenar(eAlmacenaMedio.TagPospago, ref oVehiculo);
                        break;
                    case eFormaPago.CTTPrepago:
                    case eFormaPago.CTChPrepago:
                        DecideAlmacenar(eAlmacenaMedio.TagPrepago, ref oVehiculo);
                        break;
                    case eFormaPago.CTTUFRE:
                    case eFormaPago.CTChUFRE:
                        DecideAlmacenar(eAlmacenaMedio.TagUfre, ref oVehiculo);
                        break;
                    case eFormaPago.CTPagoDiferido:
                        DecideAlmacenar(eAlmacenaMedio.PagoDiferido, ref oVehiculo);
                        break;
                }

                switch (oVehiculo.FormaPago)
                {
                    case eFormaPago.CTTExen:
                    case eFormaPago.CTTAbono:
                    case eFormaPago.CTTOmnibus:
                    case eFormaPago.CTTCC:
                    case eFormaPago.CTTTransporte:
                    case eFormaPago.CTTPrepago:
                    case eFormaPago.CTTUFRE:
                    case eFormaPago.CTTFederado:
                        if (oVehiculo.InfoTag.LecturaManual == 'S')
                            DecideAlmacenar(eAlmacenaMedio.Manual, ref oVehiculo);
                        break;
                }


                //Analizo Sin Tomar en cuenta el evento
                //" ":Solo Categoria
                DecideAlmacenar(eAlmacenaMedio.Categoria, ref oVehiculo);

                if (oVehiculo.CodigoObservacion > 0)
                    DecideAlmacenar(eAlmacenaMedio.Observado, ref oVehiculo);

                EnviarEventoVehiculo( ref oVehiculo);

                //Incremento viol6,ABORx (si no es viaje reciente)
                ModuloBaseDatos.Instance.AlmacenarAbortadoTurno(oVehiculo, _logicaCobro.GetTurno);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        /// <summary>
        /// Genera un evento de op. abortada para la vía en modo Dinámico
        /// </summary>
        override public void OpAbortadaModoD(bool bAutomatico = false, eVehiculo eVeh = eVehiculo.eVehP1, string nroTag = "" )
        {
            throw new NotImplementedException();
        }

        public override void EliminarTagManualD(string sNumero)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Limpia veh ing de la cola de vehiculos
        /// </summary>
        public override int LimpiarVehIng()
        {
            _vVehiculo[(int)eVehiculo.eVehIng] = new Vehiculo();
            return (int)eVehiculo.eVehIng;
        }

        public override void SetFlagOcupado(int nVeh, bool bOcup, ulong numeroVehiculo)
        {
            _vVehiculo[(int)eVehiculo.eVehIng].Ocupado = bOcup;
            _vVehiculo[(int)eVehiculo.eVehIng].NumeroVehiculo = numeroVehiculo;
        }

        public override bool UltimoAnuladoEsIgual(string sPatente)
        {
            return true;
        }

        public override void IniciarTimerApagadoCampanaPantalla(int? tiempoMseg)
        {

        }

        override public void ActualizarEstadoSensoresEscape()
        {

        }

        private Thread _ActualizarEstadoSensoresThread = null;

        private void ActualizarEstadoSensoresThread()
        {
            _logger.Trace("ActualizarEstadoSensoresThread - INICIO");
#if DEBUG
            // Create new stopwatch.
            Stopwatch stopwatch = new Stopwatch();
            // Begin timing.
            stopwatch.Start();
#endif
            Sensor sensor = null;

            // Sensor de Altura
            sensor = new Sensor();
            sensor.CodigoSensor = "ALT";
            sensor.NumeroSensor = 0;

            if (ModuloBaseDatos.Instance.ConfigVia.DetectorAltura != 'S')
                sensor.Estado = "";
            else
                sensor.Estado = DAC_PlacaIO.Instance.ObtenerFalloAltura() ? "B" : "A";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");

            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Barrera de entrada
            sensor = new Sensor();
            sensor.CodigoSensor = "BAR";
            sensor.NumeroSensor = 1;

            enmEstadoBarrera estadoBarrera = DAC_PlacaIO.Instance.ObtenerBarreraEntrada();

            if (estadoBarrera == enmEstadoBarrera.Nada)
                sensor.Estado = "";
            else if (estadoBarrera == enmEstadoBarrera.Arriba)
                sensor.Estado = "L";
            else if (estadoBarrera == enmEstadoBarrera.Abajo)
                sensor.Estado = "B";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");

            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Barrera de salida
            sensor = new Sensor();
            sensor.CodigoSensor = "BAR";
            sensor.NumeroSensor = 2;

            estadoBarrera = DAC_PlacaIO.Instance.ObtenerBarreraSalida(eTipoBarrera.Via);

            if (estadoBarrera == enmEstadoBarrera.Nada)
                sensor.Estado = "";
            else if (estadoBarrera == enmEstadoBarrera.Arriba)
                sensor.Estado = "L";
            else if (estadoBarrera == enmEstadoBarrera.Abajo)
                sensor.Estado = "B";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");

            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Barrera Óptica
            /*sensor = new Sensor();
            sensor.CodigoSensor = "BOP";
            sensor.NumeroSensor = 0;
            sensor.Estado = "";
            ModuloEventos.Instance.ActualizarSensores( sensor );*/

            // Sensor de ejes levantados
            sensor = new Sensor();
            sensor.CodigoSensor = "EJE";
            sensor.NumeroSensor = 0;
            sensor.Estado = "";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // BPR
            sensor = new Sensor();
            sensor.CodigoSensor = "LAZ";
            sensor.NumeroSensor = 1;
            sensor.Estado = DAC_PlacaIO.Instance.EstaOcupadoBuclePresencia() ? "O" : "L";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // BPI
            sensor = new Sensor();
            sensor.CodigoSensor = "LAZ";
            sensor.NumeroSensor = 2;
            sensor.Estado = DAC_PlacaIO.Instance.EstaOcupadoBucleIntermedio() ? "O" : "L";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // BPA
            sensor = new Sensor();
            sensor.CodigoSensor = "LAZ";
            sensor.NumeroSensor = 3;
            short retorno = 0;
            byte valor = 0;
            sensor.Estado = DAC_PlacaIO.Instance.EstaOcupadoBucleSalida(ref retorno, ref valor) ? "O" : "L";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);


            // Peanas Simples
            valor = 0;
            DAC_PlacaIO.Instance.ObtenerPeanas(ref valor);

            int estadoPeanas = valor;

            int peanasConfiguradas = int.Parse(ModuloBaseDatos.Instance.ConfigVia.ContadorEjes.ToString());

            //foreach(Peanas item in Enum.GetValues( typeof( Peanas ) ) )
            for (int i = 0; i < peanasConfiguradas; i++)
            {
                sensor = new Sensor();
                sensor.CodigoSensor = "PEA";
                estadoPeanas >>= i;
                estadoPeanas &= 0x01;

                sensor.NumeroSensor = (byte)(i + 1);
                sensor.Estado = estadoPeanas > 0 ? "O" : "L";

                _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
                ModuloEventos.Instance.ActualizarSensores(sensor);

                estadoPeanas = valor;
            }

            // Peanas dobles
            int detectoresRuedasDobles = int.Parse(ModuloBaseDatos.Instance.ConfigVia.DetectorRuedasDobles.ToString());

            // 5-7
            sensor = new Sensor();
            sensor.CodigoSensor = "PED";
            sensor.NumeroSensor = 57;

            if (detectoresRuedasDobles < 1)
                sensor.Estado = "";
            else
                sensor.Estado = "";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // 6-8
            sensor = new Sensor();
            sensor.CodigoSensor = "PED";
            sensor.NumeroSensor = 68;

            if (detectoresRuedasDobles < 2)
                sensor.Estado = "";
            else
                sensor.Estado = "";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Separador de entrada
            sensor = new Sensor();
            sensor.CodigoSensor = "SEP";
            sensor.NumeroSensor = 1;

            if (DAC_PlacaIO.Instance.ObtenerSeparador() == enmEstadoSeparador.No)
                sensor.Estado = "";
            else if (DAC_PlacaIO.Instance.ObtenerSeparador() == enmEstadoSeparador.Libre)
                sensor.Estado = "L";
            else if (DAC_PlacaIO.Instance.ObtenerSeparador() == enmEstadoSeparador.Ocupado)
                sensor.Estado = "O";
            else if (DAC_PlacaIO.Instance.ObtenerSeparador() == enmEstadoSeparador.Error)
                sensor.Estado = "F";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Separador de salida manual
            sensor = new Sensor();
            sensor.CodigoSensor = "SEP";
            sensor.NumeroSensor = 2;
            sensor.Estado = "";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Separador de salida dinámica
            sensor = new Sensor();
            sensor.CodigoSensor = "SEP";
            sensor.NumeroSensor = 3;
            sensor.Estado = "";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Semáforo de marquesina
            sensor = new Sensor();
            sensor.CodigoSensor = "SMF";
            sensor.NumeroSensor = 1;

            if (DAC_PlacaIO.Instance.ObtenerSemaforoMarquesina() == eEstadoSemaforo.Nada)
                sensor.Estado = "";
            else if (DAC_PlacaIO.Instance.ObtenerSemaforoMarquesina() == eEstadoSemaforo.Rojo)
                sensor.Estado = "R";
            else if (DAC_PlacaIO.Instance.ObtenerSemaforoMarquesina() == eEstadoSemaforo.Verde)
                sensor.Estado = "V";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);

            // Semáforo de paso
            sensor = new Sensor();
            sensor.CodigoSensor = "SMF";
            sensor.NumeroSensor = 2;

            if (DAC_PlacaIO.Instance.ObtenerSemaforoPaso() == eEstadoSemaforo.Nada)
                sensor.Estado = "";
            else if (DAC_PlacaIO.Instance.ObtenerSemaforoPaso() == eEstadoSemaforo.Rojo)
                sensor.Estado = "R";
            else if (DAC_PlacaIO.Instance.ObtenerSemaforoPaso() == eEstadoSemaforo.Verde)
                sensor.Estado = "V";

            _logger.Trace($"ActualizarEstadoSensoresThread - Sensor:[{sensor.CodigoSensor}], Nro:[{sensor.NumeroSensor}], : Estado:[{sensor.Estado}]");
            ModuloEventos.Instance.ActualizarSensores(sensor);
#if DEBUG
            // Stop timing.
            stopwatch.Stop();
            _logger.Debug("ActualizarEstadoSensoresThread() : Tiempo ejecucion [{0} ms]", stopwatch.ElapsedMilliseconds);
            stopwatch.Reset();
#endif
            _logger.Trace("ActualizarEstadoSensoresThread - FIN");
        }

        public override void ActualizarEstadoSensores( bool joinThread = false )
        {
            try
            {
                if (_ActualizarEstadoSensoresThread != null && _ActualizarEstadoSensoresThread.IsAlive &&
                    _ActualizarEstadoSensoresThread.Join(TimeSpan.FromSeconds(10)))
                {
                    _ActualizarEstadoSensoresThread.Abort();
                }
                if (_ActualizarEstadoSensoresThread == null || !_ActualizarEstadoSensoresThread.IsAlive)
                {
                    _ActualizarEstadoSensoresThread = new Thread(ActualizarEstadoSensoresThread);
                    _ActualizarEstadoSensoresThread.Start();

                    if (joinThread)
                        _ActualizarEstadoSensoresThread.Join();
                }
            }
            catch (ThreadStartException)
            {
                //No loguear esta excepcion
            }
            catch (ThreadAbortException)
            {
                //No loguear esta excepcion
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        public override void LimpiarVehEscape()
        {
           
        }

        public override void Dispose()
        {
            
        }

        public override void RegularizarFilaVehiculos()
        {
            
        }

        public override void AdelantarFilaVehiculosDesde(eVehiculo inicio)
        {

        }
            
        
    }
}
  

