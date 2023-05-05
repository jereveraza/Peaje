using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Entidades;
using Entidades.Logica;
using Comunicacion;
using Entidades.Interfaces;
using ModuloDAC_PLACAIO.Señales;
using Entidades.Comunicacion;
using ModuloDAC_PLACAIO.DAC;
using ModuloDAC_PLACAIO.Mensajes;
using Utiles;
using Entidades.ComunicacionAntena;
using Entidades.ComunicacionVideo;
using Entidades.ComunicacionFoto;
using Entidades.ComunicacionBaseDatos;
using Newtonsoft.Json;
using Entidades.ComunicacionEventos;
using Alarmas;
using Entidades.ComunicacionChip;
using System.Threading;
using NLog;

namespace ModuloLogicaVia.Logica
{
    public static class StringExt
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }

    public class VehEliminar
    {
        public bool HayVehiculo { get; set; }
        public string NumeroTag { get; set; }
        public ulong NumeroTransito { get; set; }
        public DateTime Fecha { get; set; }

        public void Clear()
        {
            HayVehiculo = false;
            NumeroTag = "";
            NumeroTransito = 0;
            Fecha = DateTime.MinValue;
        }
    }

    public partial class LogicaViaDinamica : LogicaVia
    {

        #region VARIABLES PRIVADAS

        private ILogicaCobro _logicaCobro = null;
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger("LogicaVia");
        private static readonly NLog.Logger _loggerSensores = NLog.LogManager.GetLogger("Sensores");
        private NLog.Logger _loggerTransitos = NLog.LogManager.GetLogger("Transitos");
        private NLog.Logger _loggerExcepciones = NLog.LogManager.GetLogger("excepciones");

        private System.Timers.Timer _timerApagadoCampana = new System.Timers.Timer();
        private System.Timers.Timer _timerBarreraLevantada = new System.Timers.Timer();
        private System.Timers.Timer _timerFilaVehiculos = new System.Timers.Timer();
        private bool _bEnLazoPresencia = false;
        private bool _ultimoSentidoEsOpuesto = false;
        private int _comitivaVehPendientes = 0;
        private bool _sentidoOpuesto = false;
        private bool _bMismoTag = false;
        private bool _bInitAntenaOK = true;
        private List<InfoTagLeido> _lstInfoTagLeido = new List<InfoTagLeido>();
        private List<Tag> _lstTagLeidoAntena = new List<Tag>();
        private InfoTagLeido _infoTagLeido = new InfoTagLeido();
        private InfoTag _infoTagEnCola = new InfoTag();
        private InfoTag _ultTagCancelado = new InfoTag();
        private ulong _ultVehiculoAntena = 0;
        eCausaLecturaTag _causaLecturaTag = eCausaLecturaTag.eCausaNada;
        private Vehiculo[] _vVehiculo = new Vehiculo[Vehiculo.GetMaxVehVector()];
        private TimeSpan _timeoutEnCola = new TimeSpan(0, 0, 4, 0);
        private DateTime _tiempoLecturaTagEnCola = DateTime.Now;
        private string _tagEnProceso = "";
        private string _ultTagValidado = "";
        private string _ultTagLeido = "";
        private string _ultTagErrorTag = "";
        private DateTime _ultTagValidadoTiempo = DateTime.MinValue;
        private DateTime _ultTagLeidoTiempo = DateTime.MinValue;
        private DateTime _tiempoDesactivacionAntena = DateTime.MinValue;
        private TimeSpan _tiempoDesactAntD = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(ClassUtiles.LeerConfiguracion("TAG", "TIEMPO_DESACT_ANT_D")));
        private TimeSpan _tiempoDesactAntDBPR = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(ClassUtiles.LeerConfiguracion("TAG", "TIEMPO_DESAC_ANT_DBPR")));
        private int _timeoutTagsinVeh = 0, _tiempoUltimaLecturaTag = 0;

        //private ulong _violAutista = 0;
        private char _estadoAbort = ' '; //Se usa en el Online
        private string _huellaDAC = "";
        private bool _sinPeanas = false;
        private bool _falloAltura = false; 	//Indica si al momento de pagar el sensor de altura estaba en falla
        private ulong _numeroTransito = 0;
        private int _contSuspensionTurno = 0;
        private short _catego_Autotabular = 2;
        private List<InfoMedios> _ultimoVideo = null;
        private InfoDAC _ultimoDac = new InfoDAC();

        private bool _bUltEstadoActivAntena = false;  //Flag para indicar si la invocación
                                                      //anterior a este método se realizó con 
                                                      //bActivacionAntena en true
        private eVehiculo _eVehiculoIngAnt = eVehiculo.eVehOnLine; //Vehículo Ing anterior. Sirve para saber
                                                                   //si hay que actualizar los datos en el dpy
        private ulong _ulVehiculoIngAnt = 0;  //Vehículo Ing anterior. Sirve para saber


        /// <summary>
        /// Lockeo para uso de lista de Tags
        /// </summary>
        private object _lockAsignarTag = new object();
        private object _lockModificarListaTag = new object();
        private object _lockAsignarTagLeido = new object();

        System.Timers.Timer _timerTimeoutAntena = new System.Timers.Timer();
        System.Timers.Timer _timerDesactivarAntena = new System.Timers.Timer();
        System.Timers.Timer _timerObtenerVelocidad = new System.Timers.Timer();
        System.Timers.Timer _timerBorrarTagsViejos = new System.Timers.Timer();
        private string _tagMarchaAtras = "";

        private bool _enLazoSal = false;
        private bool _habiCatego = false;
        private bool _flagCatego = false;
        private bool _statusBarreraQ = false;
        private bool _enSeparSal = false;
        private DateTime FecUltimoTransito = DateTime.MinValue;
        private bool _pendienteAutorizacionPaso = false;
        private bool _tengoTagEnProceso = false;
        private int _ucCatego_IngTag;
        private int _sCatego_Confirmada;
        private InfoDAC _infoDACOnline;
        private static bool _procesaTagIpico = false;
        private int _tiempoDescarteFotosReversaSeg, _tiempoDescarteVideosReversaSeg, _tiempoDescarteImagenesAlmacenarMin;
        private Vehiculo _VehPrimero = new Vehiculo(), _VehSegundo = new Vehiculo(), _VehTercero = new Vehiculo();
        private VehEliminar _vehiculoAEliminar = new VehEliminar();
        private bool bAsignandoTagAVehiculo = false;
        #endregion

        #region PROPIEDADES
        override public bool UltimoSentidoEsOpuesto { get { return _ultimoSentidoEsOpuesto; } set { _ultimoSentidoEsOpuesto = value; } }
        override public int ComitivaVehPendientes { get { return _comitivaVehPendientes; } set { _comitivaVehPendientes = value; } }

        override public void SetLogicaCobro(ILogicaCobro logicaCobro)
        {
            _logicaCobro = logicaCobro;
        }


        public override Vehiculo GetPrimerVehiculo()
        {
            Vehiculo eRet = null;
            int i;
            bool bEnc;

            //Empiezo a buscar desde C0 a P3 el vehículo que esté ocupado y no tenga
            //forma de pago
            i = (int)eVehiculo.eVehC0;
            bEnc = false;
            while (!bEnc && i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).NoVacio && !GetVehiculo((eVehiculo)i).ProcesandoViolacion)
                    bEnc = true;
                if (!bEnc)
                    i--;
            }

            if (bEnc)
                eRet = _vVehiculo[i];
            else
                eRet = _vVehiculo[(int)eVehiculo.eVehP1];

            return eRet;
        }

        public override Vehiculo GetPrimeroSegundoVehiculo(out bool esPrimero)
        {
            Vehiculo eRet = null;
            int i;
            bool bEnc;
            esPrimero = true;

            //Empiezo a buscar desde C0 a P3 el vehículo que esté ocupado y no tenga
            //forma de pago
            i = (int)eVehiculo.eVehC0;
            bEnc = false;
            while (!bEnc && i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).NoVacio)
                {
                    if (esPrimero)
                    {
                        //Si esta pagado y sobre el lazo, buscamos el segundo
                        if ((GetVehiculo((eVehiculo)i).SalidaON && (GetVehiculo((eVehiculo)i).EstaPagado || GetVehiculo((eVehiculo)i).PasoEnQuiebre)))
                        {
                            esPrimero = false;
                        }
                        else
                        {
                            bEnc = true;
                        }
                    }
                    else
                    {
                        bEnc = true;
                    }
                }
                if (!bEnc)
                    i--;
            }

            if (bEnc)
                eRet = _vVehiculo[i];
            else
                eRet = _vVehiculo[(int)eVehiculo.eVehP1];

            return eRet;
        }
        public override Vehiculo GetVehiculoAnterior()
        {
            return GetVehAnt();
        }

        public override bool GetHayVehiculosPagados()
        {
            bool bRet = false;
            int i;
            bool bEnc;

            //Empiezo a buscar desde C0 a P3 el vehículo que esté ocupado y no tenga
            //forma de pago
            i = (int)eVehiculo.eVehC0;
            bEnc = false;
            while (!bEnc && i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).EstaPagado || GetVehiculo((eVehiculo)i).PasoEnQuiebre)
                {
                    bRet = true;
                    bEnc = true;
                }

                i--;
            }


            return bRet;
        }

        override public Vehiculo GetVehIngCat()
        {
            return GetVehIng(false, false, true);
        }
        override public Vehiculo GetVehIng(bool bSinPagadosEnBPA = false, bool bSinBPA = false, bool bSinPagados = false)
        {
            return _vVehiculo[(int)GetVehiculoIng(bSinPagadosEnBPA, bSinBPA, bSinPagados)];
        }
        private eVehiculo GetVehiculoIng(bool bSinPagadosEnBPA = false, bool bSinBPA = false, bool bSinPagados = false)
        {
            eVehiculo eRet = eVehiculo.eVehOnLine;
            int i;
            bool bEnc;

            //Empiezo a buscar desde C0 a P3 el vehículo que esté ocupado y no tenga
            //forma de pago
            i = (int)eVehiculo.eVehC0;
            bEnc = false;
            while (!bEnc && i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).NoVacio)
                    //Si lo habilitaron en quiebre lo consideramos como pagado
                    if (!(GetVehiculo((eVehiculo)i).SalidaON && (GetVehiculo((eVehiculo)i).EstaPagado || GetVehiculo((eVehiculo)i).PasoEnQuiebre))
                            || !bSinPagadosEnBPA)
                        if (!(GetVehiculo((eVehiculo)i).SalidaON)
                                || !bSinBPA)
                            //Si lo habilitaron en quiebre y ya esta en el BPA lo consideramos como pagado
                            if (!(GetVehiculo((eVehiculo)i).EstaPagado || (GetVehiculo((eVehiculo)i).SalidaON && GetVehiculo((eVehiculo)i).PasoEnQuiebre))
                                    || !bSinPagados)
                                bEnc = true;
                if (!bEnc)
                    i--;
            }

            if (bEnc)
                eRet = (eVehiculo)i;
            else
                eRet = eVehiculo.eVehP1;

            return eRet;
        }
        override public Vehiculo GetVehTran()
        {
            return _vVehiculo[(int)GetVehiculoIng()];
        }
        override public Vehiculo GetVehAnt()
        {
            return _vVehiculo[(int)eVehiculo.eVehAnt];
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
        override public Vehiculo GetVehEscape()
        {
            return _vVehiculo[(int)eVehiculo.eVehEscape];
        }
        override public void LimpiarVehEscape()
        {
            _vVehiculo[(int)eVehiculo.eVehEscape] = new Vehiculo();
        }
        public eVehiculo GetVehIngEnum(ref Vehiculo oVehiculo)
        {
            for (int i = (int)eVehiculo.eVehP3; i <= (int)eVehiculo.eVehC0; i++)
            {
                if (_vVehiculo[i] == oVehiculo)
                    return (eVehiculo)i;
            }
            return GetVehiculoIng();
        }
        override public bool AsignandoTagAVehiculo
        {
            get { return bAsignandoTagAVehiculo; }
        }

        public bool TicketEnProceso { get; set; }
        public bool _flagIniViol { get; private set; }
        #endregion

        public LogicaViaDinamica()
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
            MessageQueue.Instance.SensorBidiAlto = null;
            MessageQueue.Instance.SensorBidiBajo = null;
            MessageQueue.Instance.LazoEscapeEgreso = null;
            MessageQueue.Instance.LazoEscapeIngreso = null;
            MessageQueue.Instance.PulsadorEscape = null;
            MessageQueue.Instance.SensorTamperAlto = null;
            MessageQueue.Instance.SensorTamperBajo = null;

            //Sobrecargo con lo mio
            MessageQueue.Instance.InicioColaOn += InicioColaOn;
            MessageQueue.Instance.InicioColaOff += InicioColaOff;
            MessageQueue.Instance.LazoPresenciaIngreso += LazoPresenciaIngreso;
            MessageQueue.Instance.LazoPresenciaEgreso += LazoPresenciaEgreso;
            MessageQueue.Instance.LazoSalidaIngreso += LazoSalidaIngreso;
            MessageQueue.Instance.LazoSalidaEgreso += LazoSalidaEgreso;
            MessageQueue.Instance.SeparadorSalidaIngreso += SeparadorSalidaIngreso;
            MessageQueue.Instance.SeparadorSalidaEgreso += SeparadorSalidaEgreso;
            MessageQueue.Instance.SensorBidiAlto += SensorBidiAlto;
            MessageQueue.Instance.SensorBidiBajo += SensorBidiBajo;
            MessageQueue.Instance.LazoEscapeEgreso += LazoEscapeEgreso;
            MessageQueue.Instance.LazoEscapeIngreso += LazoEscapeIngreso;
            MessageQueue.Instance.PulsadorEscape += PulsadorEscape;
            MessageQueue.Instance.SensorTamperAlto += OnPlatOpen;
            MessageQueue.Instance.SensorTamperBajo += OnPlatClose;

            ModuloPantalla.Instance.ProcesarFilaVehiculos -= EnviarFilaVehiculosPantalla;
            ModuloPantalla.Instance.ProcesarFilaVehiculos += EnviarFilaVehiculosPantalla;

            //Seteo la configuración en el pic
            DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.Dinamico);

            for (int i = 0; i < Vehiculo.GetMaxVehVector(); i++)
                _vVehiculo[i] = new Vehiculo();

            //_tiempoDesactAntD = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(Utiles.LeerConfiguracion("TAG", "TIEMPO_DESACT_ANT_D")));
            //_tiempoDesactAntDBPR = new TimeSpan(0, 0, 0, 0, Convert.ToInt32(Utiles.LeerConfiguracion("TAG", "TIEMPO_DESAC_ANT_DBPR")));

            string sDesactivarAntenaTimeout = ClassUtiles.LeerConfiguracion("TAG", "TIMEOUT_DESAC_ANT_D");

            int timeout;
            bool bParse = int.TryParse(ClassUtiles.LeerConfiguracion("TAG", "TIMEOUT_TAG_SIN_VEH"), out timeout);
            _timeoutTagsinVeh = bParse ? timeout : 15000;

            bParse = int.TryParse(ClassUtiles.LeerConfiguracion("TAG", "TIEMPO_ULTIMA_LECTURA_TAG"), out timeout);
            _tiempoUltimaLecturaTag = bParse ? timeout : 500;

            long desactivarAntenaTimeout = 30000;

            bParse = int.TryParse(ClassUtiles.LeerConfiguracion("DATOS", "TiempoDescarteFotosReversaSeg"), out timeout);
            _tiempoDescarteFotosReversaSeg = bParse ? timeout : 5;

            bParse = int.TryParse(ClassUtiles.LeerConfiguracion("DATOS", "TiempoDescarteVideosReversaSeg"), out timeout);
            _tiempoDescarteVideosReversaSeg = bParse ? timeout : 5;

            bParse = int.TryParse(ClassUtiles.LeerConfiguracion("DATOS", "TiempoDescarteImagenesAlmacenarMin"), out timeout);
            _tiempoDescarteImagenesAlmacenarMin = bParse ? timeout : 10;

            if (!string.IsNullOrEmpty(sDesactivarAntenaTimeout))
            {
                desactivarAntenaTimeout = long.Parse(sDesactivarAntenaTimeout);
            }

            _timerTimeoutAntena.Elapsed += new ElapsedEventHandler(onTimeoutAntena);
            _timerTimeoutAntena.Interval = desactivarAntenaTimeout;
            _timerTimeoutAntena.AutoReset = false;
            _timerTimeoutAntena.Enabled = false;

            _timerDesactivarAntena.Elapsed += new ElapsedEventHandler(onDesactivarAntena);
            _timerDesactivarAntena.Interval = 10;
            _timerDesactivarAntena.AutoReset = false;
            _timerDesactivarAntena.Enabled = false;

            _timerObtenerVelocidad.Elapsed += new ElapsedEventHandler(onObtenerVelocidad);
            _timerObtenerVelocidad.Interval = 1500;
            _timerObtenerVelocidad.AutoReset = false;
            _timerObtenerVelocidad.Enabled = false;

            _timerBarreraLevantada.Elapsed += new ElapsedEventHandler(OnTimerBarreraLevantada);
            _timerBarreraLevantada.Interval = 30000;
            _timerBarreraLevantada.AutoReset = true;
            _timerBarreraLevantada.Enabled = true;

            _timerFilaVehiculos.Elapsed += new ElapsedEventHandler(OnTimerFilaVehiculos);
            _timerFilaVehiculos.Interval = 300;
            _timerFilaVehiculos.AutoReset = true;
            _timerFilaVehiculos.Enabled = false;

            _timerBorrarTagsViejos.Elapsed += new ElapsedEventHandler(OnTimerBorrarTagsViejos);
            _timerBorrarTagsViejos.Interval = 1000;
            _timerBorrarTagsViejos.AutoReset = true;
            _timerBorrarTagsViejos.Enabled = true;

            ModuloAntena.Instance.ProcesarLecturaTag += ProcesarLecturaTag;
            ModuloOCR.Instance.ProcesarLecturaOCR += OnLecturaPatenteOCR;
            InitOCR();
            _vehiculoAEliminar.Clear();

            bAsignandoTagAVehiculo = false;
        }

        private void ProcesarTarjetaChip(eEstadoAntena estado, RespuestaChip respuesta, eTipoLecturaTag tipoLectura)
        {
            Tag oTag = new Tag();
            oTag.NumeroTag = respuesta.LecturaTarjeta.Dispositivo;
            oTag.HoraLectura = DateTime.Now;

            ProcesarLecturaTag(estado, oTag, tipoLectura);
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

        /// <summary>
        /// Cada vez que se lea un tag, se llama este delegado
        /// </summary>
        /// <param name="estado">Estado de la lectura</param>
        /// <param name="tag">Objeto que contiene el tag y el tid. Puede ser NULL</param>
        public override void ProcesarLecturaTag(eEstadoAntena estado, Tag tag, eTipoLecturaTag tipoLectura, TagBD tagManualOnline = null, Vehiculo oVehOpcional = null)
        {
            bool bUsarDatosVehiculo = false;
            Vehiculo oVehiculo = null;
            Vehiculo oVehiculoA = null;
            TagBD tagBD = new TagBD();
            TagBD tagBDOnline = new TagBD();

            try
            {
                if (tag != null)
                {
                    _logger.Info("ProcesarLecturaTag -> Inicio Tag[{Name}] TID[{Name}] tipoLectura[{Name}]", tag.NumeroTag, tag.NumeroTID, tipoLectura.ToString());

                    //Indico que estoy procesando un tag, si hago un return lo paso a false
                    _tengoTagEnProceso = true;

                    DateTime dtTiempoLectura = tag.HoraLectura;
                    InfoTag oInfoTag = new InfoTag(tag);

                    //Lo agrego a una lista de repetidos
                    if (!_lstTagLeidoAntena.Contains(tag))
                    {
                        if (tipoLectura != eTipoLecturaTag.Manual && tipoLectura != eTipoLecturaTag.Chip)
                            _lstTagLeidoAntena.Add(tag);
                    }
                    else
                    {
                        if (tipoLectura == eTipoLecturaTag.Antena)
                        {
                            _logger.Info("Ya tengo el tag leido [{name}], salgo de ProcesarLecturaTag", tag.NumeroTag);
                            _tengoTagEnProceso = false;
                            return;
                        }

                    }

                    //De acuerdo al tipo de lectura de tag, hago cosas particulares
                    if (tipoLectura == eTipoLecturaTag.Antena)
                    {
                        oInfoTag.LecturaManual = 'N';
                        oVehiculo = GetVehiculo(eVehiculo.eVehP1);
                        oVehiculoA = GetVehiculo(eVehiculo.eVehC1);

                        bUsarDatosVehiculo = oVehiculo.PuedeAsignarTag(dtTiempoLectura);
                        oInfoTag.TipOp = 'T';

                        //Saco una foto del momento en el que llega el tag por antena y la agrego al oInfoTag
                        //oInfoTag.InfoMedios = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, eCausaVideo.TagLeidoPorAntena);
                        //ModuloFoto.Instance.SacarFoto(new Vehiculo(), eCausaVideo.TagLeidoPorAntena, false, oInfoTag.InfoMedios);
                    }
                    else if (tipoLectura == eTipoLecturaTag.Manual)
                    {
                        //utilizamos el mismo vehiculo al que se le agregó la recarga
                        if (oVehOpcional != null && oVehOpcional.ListaRecarga.Any(x => x != null))
                        {
                            oInfoTag.LecturaManual = oVehOpcional.LecturaManual;
                            oVehiculo = oVehOpcional;
                            oVehiculoA = oVehOpcional;
                        }
                        else
                        {
                            oInfoTag.LecturaManual = 'S';
                            oVehiculo = GetVehIng();
                            oVehiculoA = GetVehIng();
                        }
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'T';
                    }
                    else if (tipoLectura == eTipoLecturaTag.Chip)
                    {
                        oInfoTag.LecturaManual = 'N';
                        oVehiculo = GetVehIng();
                        oVehiculoA = GetVehIng();
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'C';
                    }
                    else if (tipoLectura == eTipoLecturaTag.OCR)
                    {
                        oInfoTag.LecturaManual = 'O';
                        oVehiculo = GetVehIng();
                        oVehiculoA = GetVehIng();
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'O'; //GAB: Confirmar si está bien que cambie TipOp por ser OCR
                    }
                    else
                    {
                        oInfoTag.LecturaManual = 'N';
                        oVehiculo = GetVehIng();
                        oVehiculoA = GetVehIng();
                        bUsarDatosVehiculo = true;
                        oInfoTag.TipOp = 'T';
                    }

                    oVehiculo.LecturaManual = oInfoTag.LecturaManual;

                    //ModuloPantalla.Instance.LimpiarMensajes();

                    //Valido si el tag no se leyo hace poco tiempo o esta siendo procesado. Internamente busca en la base de datos.
                    eErrorTag errorTag = ValidarTag(ref oInfoTag, ref oVehiculo, ref oVehiculoA, false, bUsarDatosVehiculo);
                    if ((errorTag == eErrorTag.NoError && !oVehiculo.ListaRecarga.Any(x => x != null)) || (oVehiculo.ListaRecarga.Any(x => x != null) && oVehiculo.InfoTag.NumeroTag != oInfoTag.NumeroTag))
                    {
                        if (tagManualOnline == null)
                            tagBDOnline = ModuloBaseDatos.Instance.ObtenerTagEnLinea(oInfoTag.NumeroTag.Trim(), "O", oInfoTag.TipOp.ToString());
                        else
                            tagBDOnline = tagManualOnline;

                        oInfoTag.OrigenSaldo = 'O';

                        //Continuamos con la validacion local
                        if (tagBDOnline?.EstadoConsulta != EnmStatusBD.OK && tagBDOnline?.EstadoConsulta != EnmStatusBD.SINRESULTADO && tagManualOnline == null)
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

                            oInfoTag.OrigenSaldo = 'L';
                        }
                        else
                            tagBD = tagBDOnline;

                        // Si la consulta no se realizó remuevo el ultimoLeido para permitir que se vuelva a buscar,
                        // si no se hace esto quedará como repetido durante el tiempo estipulado a pesar de que nunca se buscó
                        if (tagBD?.EstadoConsulta != EnmStatusBD.OK && tagBD?.EstadoConsulta != EnmStatusBD.SINRESULTADO)
                            SetMismoTagIpico();

                        _logger.Info($"TAG[{tag.NumeroTag}] - PATENTE[{tagBD.Patente}]");

                    }
                    else
                    {
                        if (errorTag == eErrorTag.Repetido && oVehiculo.NumeroVehiculo != 0 && string.IsNullOrEmpty(oVehiculo.InfoTag.NumeroTag))
                        {
                            ModuloPantalla.Instance.LimpiarMensajes();

                            // Si oVehiculoA.InfoTag.NumeroTag == "", significa que es el siguiente vehiculo 
                            // ( y no el que esta en proceso) es el que esta dando repetido y ahi si se deberia mostrar
                            if (oVehiculoA.InfoTag.NumeroTag == string.Empty)
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "TAG REPETIDO");
                            else if (tipoLectura == eTipoLecturaTag.Chip)
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "TARJETA CHIP REPETIDA");

                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Nro: {oInfoTag.NumeroTag.Truncate(24)}");
                        }

                        if (oVehiculo.InfoTag.NumeroTag == oInfoTag.NumeroTag && oVehiculo.ListaRecarga.Any(x => x != null))
                            oInfoTag = oVehiculo.InfoTag;
                    }

                    //_tagEnProceso = "";

                    //Al setear el error, se verifica si el tag esta habilitado o no.
                    oInfoTag.ErrorTag = errorTag;
                    oInfoTag.FechaPago = DateTime.Now;

                    if (oInfoTag.ErrorTag != eErrorTag.EnProceso || tipoLectura != eTipoLecturaTag.Antena)
                    {
                        if (oVehiculoA.InfoTag.NumeroTag != oInfoTag.NumeroTag || (tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.Chip))
                        {
                            if (!(oVehiculo.InfoTag.TagOK && errorTag == eErrorTag.Desconocido) || !oVehiculo.InfoTag.TagOK)
                            {
                                //Si el tag es del sistema ya está, sino podria tener otro tag
                                //GAB: No apagar la antena por lectura de un tag habilitado hasta que ese tag se asigne a un vehiculo
                                /*if (oInfoTag.ErrorTag != eErrorTag.Desconocido && oInfoTag.ErrorTag != eErrorTag.EnProceso)
                                {
                                    //Apagamos la Antena
                                    ModuloAntena.Instance.DesactivarAntena();
                                    _logger.Info("ProcesarLecturaTag -> Desactivo Antena");
                                }*/

                                _logger.Info("ProcesarLecturaTag[{Name}]", oInfoTag.ErrorTag.GetDescription());

                                //Si es tag manual lo sacamos de todos lados
                                if ((tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.Chip) && !oVehiculo.ListaRecarga.Any(x => x != null))
                                {
                                    EliminarTagManualD(oInfoTag.NumeroTag);
                                }
                                else if (tipoLectura == eTipoLecturaTag.OCR)
                                {

                                }
                                else
                                {

                                }

                                //Asigno el tag al vehículo, y es un tag diferente o la causa es diferente (y no es repetido)
                                if (tipoLectura == eTipoLecturaTag.Manual ||
                                    (((oVehiculo.InfoTag.NumeroTag != oInfoTag.NumeroTag) && (oInfoTag.ErrorTag != eErrorTag.Repetido)) ||
                                      oVehiculo.InfoTag.RecargaReciente == 'S'))
                                {

                                    _logger.Info("_tiempoDesactivacionAntena [{0}] dtTiempoLectura [{1}]", _tiempoDesactivacionAntena.ToString("HH:mm:ss.fff"), dtTiempoLectura.ToString("HH:mm:ss.fff"));

                                    //Si lo leimos a tiempo
                                    if (tipoLectura != eTipoLecturaTag.Chip && tipoLectura != eTipoLecturaTag.Manual && tipoLectura != eTipoLecturaTag.OCR &&
                                         _tiempoDesactivacionAntena > DateTime.MinValue &&
                                        dtTiempoLectura > _tiempoDesactivacionAntena)
                                    {
                                        _logger.Info("Leimos tag muy tarde");

                                        //Leimos el tag muy tarde, lo dejamos pendiente para el proximo
                                        AsignarTagLeido(oInfoTag, false, true);
                                    }
                                    else
                                    {
                                        _logger.Debug("ProcesarLecturaTag -> Finaliza lectura chip");
                                        //Si esta bien finalizo la lectura
                                        ModuloTarjetaChip.Instance.FinalizaLectura();

                                        //Si está habilitado
                                        if (oInfoTag.GetTagHabilitado())
                                        {
                                            _logger.Debug("ProcesarLecturaTag -> Tag Habilitado. EstadoVia [{Name}]", _logicaCobro.Estado);
                                            if (oInfoTag.TipOp == 'T')
                                                if (oInfoTag.LecturaManual == 'S' && oInfoTag.TipoTag != eTipoCuentaTag.Ufre)
                                                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.TraTagManual);

                                            if (_logicaCobro.Estado == eEstadoVia.EVAbiertaLibre || _logicaCobro.Estado == eEstadoVia.EVAbiertaCat)
                                            {
                                                if (oInfoTag.TipoTag != eTipoCuentaTag.Ufre && tipoLectura != eTipoLecturaTag.Chip)
                                                {
                                                    if ((tipoLectura == eTipoLecturaTag.Chip && oVehiculo.ListaRecarga.Any(x => x != null)) || tipoLectura == eTipoLecturaTag.Manual)
                                                        RevisarBarreraTagManual();
                                                    else //Revisamos si hay que abrir la barrera para hacerlo lo antes posible
                                                        RevisarBarreraTag();
                                                }

                                                // Se limpia la pantalla luego de la salida del vehiculo
                                                ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);
                                            }

                                            if (_logicaCobro.Modo?.Modo != "D")
                                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.TagNoHabilitado, 0);
                                            else
                                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.TagNoHabilitadoSinCajero, 0);
                                        }
                                        else
                                        {
                                            _logger.Debug("ProcesarLecturaTag -> Tag NO Habilitado.");
                                            bool mostrarEnPantalla;

                                            if (oInfoTag.ErrorTag == eErrorTag.Desconocido)
                                            {
                                                mostrarEnPantalla = true;
                                            }
                                            else
                                            {
                                                if (tipoLectura == eTipoLecturaTag.Chip || tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.OCR)
                                                {
                                                    mostrarEnPantalla = true;
                                                }
                                                else
                                                {
                                                    if (_logicaCobro?.Modo?.Modo != "D")
                                                    {
                                                        //Mostrar solo si es el primero
                                                        if (_infoTagLeido.GetCantVehiculos() > 0 && PuedeMostrarVehiculoTag())
                                                            mostrarEnPantalla = true;
                                                        else
                                                            mostrarEnPantalla = false;
                                                    }
                                                    else
                                                        mostrarEnPantalla = false;
                                                }
                                            }

                                            //El tag no esta habilitado, envio evento
                                            EventoErrorMedioPago errorMP = new EventoErrorMedioPago();
                                            errorMP.TipoDispositivo = oInfoTag.TipOp;
                                            errorMP.Causa = errorMP.ObtenerCausa(errorTag);
                                            errorMP.LecturaManual = tipoLectura == eTipoLecturaTag.Manual;
                                            errorMP.LecturaTag = true;
                                            errorMP.Observacion = string.Format("Causa: {0} - ", errorMP.Causa);

                                            if (string.IsNullOrEmpty(oVehiculo.InfoTag.NumeroTag) || // Si es la primera vez, va el primero
                                                oVehiculo.InfoTag.ErrorTag == eErrorTag.Desconocido) // Si el primero es desconocido, siempre va el segundo

                                            {
                                                mostrarEnPantalla = true;
                                            }

                                            //Mensaje de evento Error medio de pago
                                            if (oInfoTag.ErrorTag == eErrorTag.Desconocido)
                                            {
                                                //if( _logicaCobro?.Estado == eEstadoVia.EVAbiertaLibre || _logicaCobro?.Estado == eEstadoVia.EVAbiertaCat )
                                                errorMP.Observacion += Utiles.Utiles.Traduccion.Traducir("TAG DESCONOCIDO") + $" ({oInfoTag.NumeroTag})";
                                            }
                                            else
                                            {
                                                string mensaje = string.Empty;

                                                //Si no está habilitado muestro el mensaje que tiene, NO el auxiliar, porque en el auxiliar NO tiene nada
                                                if (errorTag == eErrorTag.NoHabilitado)
                                                {
                                                    mensaje = string.IsNullOrEmpty(oInfoTag?.Mensaje) ? errorTag.ToString() : oInfoTag?.Mensaje;
                                                    mensaje += " ( " + ClassUtiles.FormatearMonedaAString(oInfoTag.SaldoFinal) + " )";
                                                }
                                                else
                                                {
                                                    mensaje = string.IsNullOrEmpty(oInfoTag?.MensajeAuxiliar) ? errorTag.ToString() : oInfoTag?.MensajeAuxiliar;
                                                    mensaje += " ( " + ClassUtiles.FormatearMonedaAString(oInfoTag.SaldoFinal) + " )";
                                                }

                                                errorMP.Observacion += mensaje + $" ({oInfoTag.NumeroTag})";
                                            }

                                            if (oInfoTag.TipoTag == eTipoCuentaTag.Prepago && oInfoTag.SaldoFinal < oInfoTag.Tarifa && tipoLectura != eTipoLecturaTag.Chip)
                                                oVehiculo.EsperaRecargaVia = true;

                                            if (_logicaCobro?.Modo?.Modo != "D")
                                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.TagNoHabilitado, 0);
                                            else
                                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.TagNoHabilitadoSinCajero, 0);

                                            if (_ultTagErrorTag != oInfoTag.NumeroTag)
                                            {
                                                _ultTagErrorTag = oInfoTag.NumeroTag;
                                                ModuloEventos.Instance.SetErrorMedioPago(_logicaCobro?.GetTurno, oInfoTag, errorMP);
                                            }
                                        }


                                        //Si es tag manual o T Chip se asigna al primer vehiculo
                                        //sino al que está en la cola
                                        if (tipoLectura == eTipoLecturaTag.OCR)
                                        {
                                            AsignarTagLeidoPorOCR(oInfoTag, 0);
                                        }
                                        else
                                        {
                                            bool manualOChip = tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.Chip;

                                            if (oVehiculo.InfoTag.NumeroTag == oInfoTag.NumeroTag && oVehiculo.ListaRecarga.Any(x => x != null))
                                                AsignarTagLeido(oInfoTag, manualOChip, manualOChip, oVehiculo.NumeroVehiculo);
                                            else
                                                AsignarTagLeido(oInfoTag, manualOChip, manualOChip);
                                        }
                                    }
                                }
                                else
                                {
                                    if ((tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.Chip) && (_logicaCobro?.Estado == eEstadoVia.EVAbiertaLibre || _logicaCobro?.Estado == eEstadoVia.EVAbiertaCat))
                                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("TAG REPETIDO"));
                                }
                            }
                            else
                            {
                                if ((tipoLectura == eTipoLecturaTag.Manual || tipoLectura == eTipoLecturaTag.Chip) && (_logicaCobro.Estado == eEstadoVia.EVAbiertaLibre || _logicaCobro.Estado == eEstadoVia.EVAbiertaCat))
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("TAG REPETIDO"));
                            }
                        }
                    }

                    ActualizarEstadoSensores();
                    UpdateOnline();

                    _tagEnProceso = "";
                    _tengoTagEnProceso = false;
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
                _tengoTagEnProceso = false;
            }

            LoguearColaVehiculos();
        }

        public void OnTimerBorrarTagsViejos(object source, ElapsedEventArgs e)
        {
            try
            {
                _timerBorrarTagsViejos.Enabled = false;
                if (_logicaCobro?.Estado != eEstadoVia.EVCerrada)
                {
                    RevisarTagsleidos();

                    TimeSpan ts = new TimeSpan(0, 0, 0, 0, _timeoutTagsinVeh);
                    int pos = 0;

                    //Saco de la lista de tags repetidos, los más viejos
                    while (pos < _lstTagLeidoAntena.Count)
                    {
                        if (_lstTagLeidoAntena.Contains(_lstTagLeidoAntena[pos]))
                        {
                            DateTime fechacomp = _lstTagLeidoAntena[pos].HoraLectura + ts;

                            int comp = DateTime.Compare(DateTime.Now, fechacomp);
                            if (comp > 0)
                            {
                                _logger.Info("Saco tag viejo [{Name}][{Name}]", _lstTagLeidoAntena[pos].NumeroTag, _lstTagLeidoAntena[pos].HoraLectura);
                                _lstTagLeidoAntena.Remove(_lstTagLeidoAntena[pos]);
                                pos = 0;
                            }
                            else
                            {
                                pos++;
                            }
                        }
                    }

                    //Recorrer fila de vehiculos de C0 a P3
                    int bVehicCola = 0;
                    Vehiculo oVehic = null;
                    TimeSpan ctsDiff;
                    for (bVehicCola = (int)eVehiculo.eVehP3; bVehicCola <= (int)eVehiculo.eVehC0; bVehicCola++)
                    {
                        oVehic = GetVehiculo((eVehiculo)bVehicCola);
                        if (oVehic != null && !oVehic.Ocupado && !string.IsNullOrEmpty(oVehic.InfoTag.NumeroTag) && !oVehic.EventoVehiculo && !oVehic.CobroEnCurso && !oVehic.CancelacionEnCurso)
                        {
                            ctsDiff = DateTime.Now - oVehic.InfoTag.FechaPago;
                            if ((oVehic.LecturaManual != 'S' && !oVehic.VentaAsociada && !oVehic.ListaRecarga.Any(x => x != null)) && ctsDiff.TotalMilliseconds >= _timeoutTagsinVeh)
                            {
                                if (oVehic.InfoTag.TipoTag != eTipoCuentaTag.Ufre ||
                                  (oVehic.InfoTag.TipoTag == eTipoCuentaTag.Ufre && !oVehic.EstaPagado))
                                    LimpiarVehiculo(oVehic, (eVehiculo)bVehicCola);
                            }
                        }
                    }
                }
                _timerBorrarTagsViejos.Enabled = true;
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
                _timerBorrarTagsViejos.Enabled = true;
            }
        }


        public override bool GetUltimoSentidoEsOpuesto()
        {
            return _ultimoSentidoEsOpuesto;
        }

        public override int GetComitivaVehPendientes()
        {
            return _comitivaVehPendientes;
        }


        /// <summary>
        /// Abre la barrera siempre para lectura manual de tag o T Chip
        /// Llamar si el ingreso es manual y el tag está habilitado
        /// </summary>
        /// <returns>true: si abrio la barrera</returns>
        private bool RevisarBarreraTagManual()
        {
            _logger.Debug("RevisarBarreraTagManual -> Inicio");

            bool bRet = true;
            GetVehOnline().InfoDac.Categoria = 0;
            GetVehOnline().CategoriaProximo = 0;
            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

            // Actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            _logger.Info("RevisarBarreraTagManual -> Tag habilitado, BARRERA ARRIBA!!");

            return bRet;
        }

        /// <summary>
        /// Si este vehiculo no tiene a nadie ocupado delante devuelve true
		/// Obtiene el numero de vehiculo de m_InfoTagLeido
        /// </summary>
        /// <returns>true: si lo puede mostrar (si era el primero de la cola)</returns>
        bool PuedeMostrarVehiculoTag()
        {
            bool bEnc, bRet;
            int i;

            ulong ulNroVehiculo;
            eVehiculo enumVehiculo;
            Vehiculo oVeh = null;

            _logger.Debug("PuedeMostrarVehiculoTag -> Inicio. CantVeh [{0}]", _infoTagLeido.GetCantVehiculos());
            bRet = false;
            //Si hay 1 solo vehiculo posible
            if (_infoTagLeido.GetCantVehiculos() == 1)
            {
                ulNroVehiculo = _infoTagLeido.GetNumeroVehiculo(0);
                _logger.Info("PuedeMostrarVehiculoTag -> Vehiculo [{Name}] ", ulNroVehiculo);
                //Busco en la lista de vehículos el vehículo con número ulNroVehiculo
                bEnc = false;
                i = (int)eVehiculo.eVehP3;
                while (!bEnc && i <= (int)eVehiculo.eVehC0)
                {
                    oVeh = GetVehiculo((eVehiculo)i);
                    if (oVeh.NumeroVehiculo == ulNroVehiculo)
                        if (!oVeh.EstaPagado)
                            //Solo lo asigno si no está pagado
                            bEnc = true;
                    i++;
                }
                if (bEnc)
                {
                    i--;
                    enumVehiculo = (eVehiculo)i;
                    //Si el vehículo 
                    // es P1 y C1 y C0 están vacíos
                    //o es C1 y C0 está vacío
                    //o es C0		
                    if (
                        (enumVehiculo == eVehiculo.eVehP1 && !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                        (enumVehiculo == eVehiculo.eVehC1 && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                        (enumVehiculo == eVehiculo.eVehC0))
                    {
                        bRet = true;
                    }
                }
            }
            _logger.Debug("PuedeMostrarVehiculoTag -> Fin");
            return bRet;
        }

        /// <summary>
        /// Quitamos el tag ingresado manualmente a cualquier vehiculo que lo tuviera
        /// o a la cola de tagas leidos
        /// </summary>
        /// <param name="sNumero">Número de Tag a Eliminar</param>
        public override void EliminarTagManualD(string sNumero)
        {
            _logger.Debug("EliminarTagManualD -> Inicio");
            int i;

            //Vehiculos en la cola
            i = (int)eVehiculo.eVehC0;
            while (i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).InfoTag.NumeroTag == sNumero)
                {
                    bool bEsTag = GetVehiculo((eVehiculo)i).InfoTag?.TipOp == 'T' ? true : false;
                    _logger.Info("EliminarTagManual->Quitamos de vehiculo [{0}] [{1}]", GetVehiculo((eVehiculo)i).NumeroVehiculo, ((eVehiculo)i).ToString());

                    //el tag se asignó a un veh de atras y le hicieron tag manual, si está pagado almacenamos info relevante que ya tenía
                    if (GetVehiculo((eVehiculo)i).EstaPagado)
                    {
                        _vehiculoAEliminar.HayVehiculo = true;
                        _vehiculoAEliminar.NumeroTag = GetVehiculo((eVehiculo)i).InfoTag.NumeroTag;
                        _vehiculoAEliminar.NumeroTransito = GetVehiculo((eVehiculo)i).NumeroTransito;
                        _vehiculoAEliminar.Fecha = GetVehiculo((eVehiculo)i).Fecha;
                        GetVehiculo((eVehiculo)i).NumeroTransito = 0;
                        GetVehiculo((eVehiculo)i).Operacion = "";
                        GetVehiculo((eVehiculo)i).ListaInfoFoto.Clear();
                    }

                    GetVehiculo((eVehiculo)i).InfoTag.Clear();
                    GetVehiculo((eVehiculo)i).ClearDatosSeteadosPorTag(bEsTag);
                }
                i--;
            }

            //Lista de Tags Leidos
            DepartTagLeido(sNumero);

            //Revisamos tambien el tag que estamos leyendo
            if (_infoTagLeido.GetInfoTag().NumeroTag == sNumero)
            {
                _logger.Info("EliminarTagManual->Quitamos de tag leido");
                _infoTagLeido.GetInfoTag().Clear();
            }

            _logger.Debug("EliminarTagManualD -> Fin");
        }


        private eErrorTag ValidarTag(ref InfoTag oInfoTag, ref Vehiculo oVehiculo, ref Vehiculo oVehiculoA, bool revalidar, bool bUsarDatosVehiculo)
        {
            eErrorTag error = eErrorTag.NoError;
            bool bViajeReciente = false;
            _logger.Info("ValidaTag -> Inicio bUsarDatosVehiculo[{Name}]", bUsarDatosVehiculo);
            try
            {
                if (oInfoTag == null)
                    return eErrorTag.Error;

                oInfoTag.Confirmado = true;

                string numeroTag = oInfoTag.NumeroTag?.Trim();
                if (string.IsNullOrEmpty(numeroTag))
                    return eErrorTag.Error;

                //TAG EN PROCESO
                if (!revalidar /*&& oInfoTag.LecturaManual != 'S'*/)
                {
                    if (!string.IsNullOrEmpty(numeroTag) && _tagEnProceso?.Trim() == numeroTag && oInfoTag.TipOp != 'C')
                    {
                        _logger.Info("ValidarTag -> TagEnProceso es igual a este");
                        return eErrorTag.EnProceso;
                    }
                }

                if (_logicaCobro.Estado == eEstadoVia.EVCerrada)
                {
                    SetMismoTagIpico();
                    _logger.Info("ValidarTag -> Via D cerrada, EXIT");
                    return eErrorTag.ViaCerrada;
                }

                if (!revalidar && oInfoTag.LecturaManual != 'S')
                {
                    if (oVehiculo != null)
                    {
                        if (!string.IsNullOrEmpty(numeroTag) && oVehiculo.InfoTag?.NumeroTag?.Trim() == numeroTag && oInfoTag?.TipOp != 'C')
                        {
                            _logger.Info("ValidarTag -> pVeh ya tiene este tag");
                            return eErrorTag.EnProceso;
                        }
                    }
                    if (oVehiculoA != null)
                    {
                        if (!string.IsNullOrEmpty(numeroTag) && oVehiculoA.InfoTag?.NumeroTag.Trim() == numeroTag && oInfoTag?.TipOp != 'C')
                        {
                            _logger.Info("ValidarTag -> pVehA ya tiene este tag");
                            return eErrorTag.EnProceso;
                        }
                    }
                }

                if (!revalidar && oInfoTag.LecturaManual != 'S')
                {
                    if (!string.IsNullOrEmpty(numeroTag) && _infoTagEnCola.NumeroTag?.Trim() == numeroTag && oInfoTag?.TipOp != 'C')
                    {
                        _tiempoLecturaTagEnCola = DateTime.Now;
                        _logger.Info("ValidarTag -> InfoTagEnCola es igual a este");
                        return eErrorTag.EnProceso;
                    }
                }

                //Si es el tag que salio marcha atras no es repetido
                if (!string.IsNullOrEmpty(numeroTag) && _tagMarchaAtras?.Trim() == numeroTag)
                {
                    bViajeReciente = true;
                    _tagMarchaAtras = "";  //Para no continuar validando
                    _logger.Info("ValidarTag -> Viaje Reciente");
                }
                else
                {
                    // ULTIMO TAG VALIDADO REPETIDO
                    if (!revalidar && oInfoTag.LecturaManual != 'S')
                    {
                        // Por defecto no permite
                        bool bPermiteRepetidos = false;
                        bool bChip = oInfoTag.TipOp == 'C';

                        if (bChip)
                            bPermiteRepetidos = _logicaCobro.ModoPermite(ePermisosModos.LecturaChipRepetido);

                        _logger.Debug("ValidarTag -> Es Tag repetido? ModoPermite?[{0}]. Es tarjeta Chip?[{1}]. Ultimo Tag Validado[{2}], InfoTagNumero[{3}]",
                                    bPermiteRepetidos ? "Si" : "No", bChip ? "Si" : "No", _ultTagValidado, oInfoTag.NumeroTag);

                        if (!string.IsNullOrEmpty(numeroTag) && _ultTagValidado?.Trim() == numeroTag && !bPermiteRepetidos/*&& oInfoTag.TipOp != 'C'*/)
                        {
                            //Si no paso el tiempo
                            if (DateTime.Now < (_ultTagValidadoTiempo + new TimeSpan(0, 0, /*m_nMinPrevTagRepetido*/2, 0))) //TODO IMPLEMENTAR el tiempo configurable
                            {
                                _logger.Info("ValidarTag -> UltTagValidado es igual a este");
                                return eErrorTag.EnProceso;
                            }
                            else
                                _logger.Debug("ValidarTag ->ultTagValidado. No pasó el tiempo");
                        }
                    }

                    // ULTIMO TAG LEIDO REPETIDO
                    if (!revalidar && oInfoTag.LecturaManual != 'S')
                    {
                        if (!string.IsNullOrEmpty(numeroTag) && _ultTagLeido?.Trim() == numeroTag && oInfoTag.TipOp != 'C')
                        {
                            //Si no paso el tiempo
                            if (DateTime.Now < (_ultTagLeidoTiempo + new TimeSpan(0, 0, /*m_nMinPrevTagRepetido*/2, 0))) //TODO IMPLEMENTAR el tiempo configurable
                            {
                                _logger.Info("ValidarTag -> UltTagLeido es igual a este");
                                return eErrorTag.Repetido;
                            }
                            else
                                _logger.Debug("ValidarTag -> ultTagLeido. No pasó el tiempo");
                        }
                        /*
                        if (_ultTagCancelado == oInfoTag.NumeroTag && oInfoTag.TipOp != 'C')
                        {
                            _logger.Info("ValidarTag -> UltTagCancelado es igual a este");
                            return eErrorTag.Repetido;
                        }
                        */
                    }
                }

                //el 1er veh canceló el tag, y entró otro veh nuevo, no asignar tag al 2do
                if (_ultTagCancelado.NumeroTag == oInfoTag.NumeroTag && GetPrimerVehiculo().TieneTagCancelado && oInfoTag.LecturaManual != 'S')
                {
                    _logger.Info("ValidarTag -> UltTagCancelado es igual a este");
                    return eErrorTag.Repetido;
                }

                //Asigno el ultimo tag leido
                _tagEnProceso = numeroTag;
                _ultTagValidado = numeroTag;
                _ultTagValidadoTiempo = DateTime.Now;
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
                error = eErrorTag.Error;
            }
            _logger.Info("ValidaTag -> Fin Tag[{Name}] Estado[{Name}]", oInfoTag.NumeroTag, error.ToString());
            return error;
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

                    // Datos del vehiculo
                    oInfoTag.TipoTag = (eTipoCuentaTag)tagBD.TipoCuenta.GetValueOrDefault();

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

                    //Estado del Tag
                    if (tagBD.Habilitado != 'S')
                        return eErrorTag.NoHabilitado;

                    oInfoTag.Confirmado = true;

                    if (_logicaCobro?.Modo?.Modo == "D")//Si es modo D seteo recorrido por defecto y la categoria tabulada.
                    {
                        //Sin cajero
                        oInfoTag.CategoTabulada = oInfoTag.Categoria;
                    }
                    else//Sino seteo categoria del vehiculo al Tag
                    {
                        if (bUsarDatosVehiculo)
                        {
                            oInfoTag.CategoTabulada = oVehiculo.Categoria;
                        }
                        else
                        {
                            oInfoTag.CategoTabulada = 1;
                        }
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

                    //Validar Tarifa

                    //UFRE o FEDERADO en via D
                    if (tagBD.PagoEnVia == 'S' && !oInfoTag.PagoEnViaPrepago)
                    {
                        if (_logicaCobro.Modo.Modo == "D")//Si es modo D seteo recorrido por defecto y la categoria tabulada.
                        {
                            return eErrorTag.NoHabilitado;
                        }
                    }

                    // Se busca la tarifa
                    short shCategoria = oInfoTag.Categoria;
                    //si es Tchip evalua si el modo permite que se use la categoria que tabulo el cajero o la cat de la tchip
                    if (oInfoTag.TipOp == 'C')
                    {
                        if (_logicaCobro.ModoPermite(ePermisosModos.TSCUsaCategoriaCajero))
                            shCategoria = oInfoTag.CategoTabulada;
                    }

                    TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(shCategoria, oInfoTag.TipoTarifa);
                    Tarifa tarifa = ModuloBaseDatos.Instance.BuscarTarifa(tarifaABuscar);

                    if (tarifa.EstadoConsulta != EnmStatusBD.OK)
                    {
                        //se intenta volver a buscar
                        tarifa = ModuloBaseDatos.Instance.BuscarTarifa(tarifaABuscar);
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

                    //Si Controla Categoria


                    //Controles Nuevos
                    //if (oInfoTag.TieneFechaVencimiento)
                    //{
                    //    if(DateTime.Now > oInfoTag.ven)
                    //        return eErrorTag.ven
                    //}

                    byte btTitar = oInfoTag.TipoTarifa;
                    if (oInfoTag.PagoEnViaPrepago)
                    {
                        //se modifican valores par apermitir validación
                        tagBD.ConSaldo = 'S';
                        tagBD.TipoSaldo = 'M';
                        oInfoTag.TipoTarifa = 0;
                    }

                    //tagBD.ConSaldo
                    if (tagBD.ConSaldo == 'S')//Si usa saldo
                    {
                        if (tagBD.TipoSaldo == 'M') //Monto
                        {
                            if (oInfoTag.PagoEnViaPrepago)
                                //si es pago en via y tiene saldo se cambia el tipBo para enviar el cobro
                                oInfoTag.TipBo = 'P';

                            if (oInfoTag.TipoTarifa > 0)
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

        override public async void LazoSalidaIngreso(bool esModoForzado, short RespDAC)
        {
            _logger.Info("******************* LazoSalidaIngreso -> Ingreso[{Name}]", RespDAC);

            LoguearColaVehiculos();

            eVehiculo eIndex;

            try
            {
                if (_enLazoSal == true && esModoForzado == false)
                    LazoSalidaEgreso(true, 0);

                _enLazoSal = true;
                _enSeparSal = true;

                //Obtengo el vehículo ING y le marco el flag de salida ON
                eIndex = GetVehiculoIng();

                Vehiculo veh = GetVehiculo(eIndex);

                // Se limpia la pantalla luego de la salida del vehiculo
                ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);
                Vehiculo vehiculo = new Vehiculo();
                vehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);

                if (_logicaCobro.GetTurno.Mantenimiento == 'S')
                    vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketNoFiscal);
                else
                    vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);

                vehiculo.FormaPago = eFormaPago.Nada;
                vehiculo.CategoDescripcionLarga = veh.CategoDescripcionLarga;
                vehiculo.Patente = string.Empty;
                vehiculo.Fecha = veh.Fecha;
                vehiculo.InfoTag = veh.InfoTag;

                List<DatoVia> listaDatosVia = new List<DatoVia>();

                if (veh.FormaPago != eFormaPago.Nada)
                {
                    ClassUtiles.InsertarDatoVia(veh, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ACTUALIZA_ULTVEH, listaDatosVia);
                }

                ModuloPantalla.Instance.LimpiarVehiculo(vehiculo);
                ModuloPantalla.Instance.LimpiarMensajes();

                GetVehiculo(eIndex).SalidaON = true;
                GetVehiculo(eIndex).SetSalidaONClock();
                LazoIngSemPaso(0, 0);
                SetVehiculoIng();

                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                listaDatosVia = new List<DatoVia>();

                //Sirena
                if (!DAC_PlacaIO.Instance.EntradaBidi())
                {
                    // Envia mensaje a display
                    ModuloDisplay.Instance.Enviar(eDisplay.BNV);

                    if (_logicaCobro.GetTurno.Mantenimiento == 'S')
                    {
                        // ConfiguracionAlarma
                        ConfigAlarma oCfgAlarma = await ModuloBaseDatos.Instance.BuscarConfiguracionAlarmaAsync("E");
                        if (oCfgAlarma != null)
                        {
                            DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Exento, oCfgAlarma.DuracionVisual, oCfgAlarma.DuracionSonido, true);
                        }
                        else
                        {
                            //Agrego esto por si falla la consulta
                            DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Exento, 1, 1, true);
                        }
                        if (!veh.EstaPagado)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("Violación"));
                        IniciarTimerApagadoCampanaPantalla(2000);
                        // Actualiza el estado de los mimicos en pantalla
                        mimicos = new Mimicos();
                        mimicos.CampanaViolacion = enmEstadoAlarma.Activa;
                        DecideCaptura(eCausaVideo.LazoSalidaOcupado, veh.NumeroVehiculo);
                    }
                    else
                    {
                        if (!veh.EstaPagado)   //Es una violacion?
                        {
                            bool activarAlarma = _logicaCobro.ModoQuiebre == eQuiebre.Nada ||
                                                 (_logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.PermisoModo[ePermisosModos.AlarmaQuiebreLiberado]);
                            // _logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.BuscarPermisoModo( (int)ePermisosModos.AlarmaQuiebreLiberado, _logicaCobro.Modo.Modo ) );

                            if (activarAlarma)
                            {
                                // ConfiguracionAlarma
                                ConfigAlarma oCfgAlarma = await ModuloBaseDatos.Instance.BuscarConfiguracionAlarmaAsync("V");
                                if (oCfgAlarma != null)
                                {
                                    DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, oCfgAlarma.DuracionVisual, oCfgAlarma.DuracionSonido, true);
                                }
                                else
                                {
                                    //Por si falla la consulta
                                    DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 1, 1, true);
                                }
                                IniciarTimerApagadoCampanaPantalla(2000);
                                // Actualiza el estado de los mimicos en pantalla
                                mimicos = new Mimicos();
                                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("Violación"));
                                ModuloPantalla.Instance.LimpiarVehiculo(veh);
                            }
                            DecideCaptura(eCausaVideo.Violacion, veh.NumeroVehiculo);
                        }
                        else
                            DecideCaptura(eCausaVideo.LazoSalidaOcupado, veh.NumeroVehiculo);
                    }
                }

                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                ActualizarEstadoSensores();
                UpdateOnline();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "LazoSalidaIngreso");
            }

            LoguearColaVehiculos();
            _logger.Info("******************* LazoSalidaIngreso -> Salida");
        }

        private void LazoIngSemPaso(short sobligado, long lobligado)
        {

            //Si esta abierto el dialog para ingresar el numero de patente
            //para el ticket de clearing lo cierro
            _logger.Info("INGRESO A LazoIngSemPaso");

            //Lo agregue en OcultarDialogos, y lo llama en OnSeparSal


            //Si el vehiculo ingresado tiene tag, el semaforo debe quedar verde
            //tambien si hay vehiculos en la cola de cobro anticipado
            if (GetVehIng().InfoTag.GetTagHabilitado()
                /*|| m_ColaVehPagoAd.GetCount() > 0*/)
            {
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
            }
            else
            {
                bool semaforoEnVerde = false;
                if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera
                    && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                {
                    semaforoEnVerde = _logicaCobro.ModoPermite(ePermisosModos.SemaforoPasoVerdeEnQuiebre);
                }

                if (!semaforoEnVerde)
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
            }

            _habiCatego = true;
            _flagCatego = false;
            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                Autotabular();
            _logger.Debug("LazoIngSemPaso -> Fin");
        }

        /* ***************************************************************** *\
            Nombre Método: Autotabular
            Parámetros: 
                bMostrarBNVModoD (entrada): muestra en mensaje de bienvenida
                                        cuando se abre un turno en modo D
                                        o la vía arranca en modo D
            Retorno:	
                Nada.
            Descripción:
                Establece segun la configuracion, la categoria correspondiente.
        \* ***************************************************************** */
        private void Autotabular(bool bMostrarBNVModoD = false)
        {
            byte byMonocategoria = 0;

            //_logger.Info("Ingresa a Autotabular()");

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


        /// <summary>
        /// Mensaje que se activa al salir del bucle de salida.
        /// </summary>
        /// <param name="esModoForzado">Parametro que indica que es Forzado (1) o No (0)</param>
        /// <param name="RespDAC"></param>
        override public void LazoSalidaEgreso(bool esModoForzado, short RespDAC)
        {
            _logger.Info("******************* LazoSalidaEgreso -> Ingreso[{Name}]", RespDAC);
            LoguearColaVehiculos();

            bool bEnc = false;
            eVehiculo eIndex;

            try
            {
                if (_enLazoSal == false && esModoForzado == false)
                    LazoSalidaIngreso(true, 0); //Se fuerza el ingreso a lazo.

                _enLazoSal = false;
                _enSeparSal = false;

                // No está ni estuvo abierta en sentido opuesto
                //Solo para cerrada

                //_logger.Info("m_bUltimoSentidoEsOpuesto[{Name}],IsSentidoOpuesto(false)[{Name}],IsSentidoOpuesto(true)[{Name}]", _ultimoSentidoEsOpuesto ? "T" : "F", IsSentidoOpuesto(false) ? "T" : "F", IsSentidoOpuesto(true) ? "T" : "F");

                if (_logicaCobro.Estado != eEstadoVia.EVCerrada || (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true)))
                {
                    //Obtengo el vehículo ING sin considerar salida ON
                    //y reseteo el flag de salida ON
                    eIndex = GetVehiculoIng();
                    GetVehiculo(eIndex).SalidaON = false;
                    GetVehiculo(eIndex).GetSalidaONClock();
                    SetVehiculoIng();

                    //Si la vía no está en QuiebreLiberado bajo la barrera
                    if (!(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera
                        && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado))
                    {
                        //Busco el primer vehículo de la cola no vacío (más viejo)
                        eIndex = eVehiculo.eVehC0;
                        bEnc = false;
                        while (!bEnc && eIndex >= eVehiculo.eVehP3)
                        {
                            bEnc = (GetVehiculo(eIndex).Init && GetVehiculo(eIndex).NoVacio);
                            if (!bEnc)
                                eIndex = (eVehiculo)((int)eIndex - 1);
                        }

                        if (bEnc)
                        {
                            _logger?.Debug("LazoSalidaEgreso -> Encuentra Vehiculo {0}", eIndex.ToString());
                            //Si tiene forma de pago habilitada levanto la barrera, sino la bajo
                            if (GetVehiculo(eIndex).EstaPagado)
                            {
                                _logger.Info("LazoSalidaEgreso -> Vehiculo [{Name}] FP [{Name}]", GetVehiculo(eIndex).NumeroVehiculo, GetVehiculo(eIndex).FormaPago.ToString());
                                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                                _logger?.Debug("LazoSalidaEgreso -> BARRERA ARRIBA!!");
                            }
                            else
                            {
                                //el primero todavía no pagó
                                //Si el bucle está libre bajamos la barrera
                                //Si este primero tiene salida falsa no movemos la barrera
                                if (!_statusBarreraQ)
                                {
                                    if (!EstaOcupadoBucleSalida()
                                        && !GetVehiculo(eIndex).SalidaFalsa)
                                    {
                                        if (!GetVehiculo(eIndex).BarreraAbierta)
                                        {
                                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                                            _logger.Info("LazoSalidaEgreso -> BARRERA ABAJO!!");
                                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                        }
                                        else
                                            _logger.Info("LazoSalidaEgreso -> Se mantiene la barrera arriba, se está asignando un tag hab 1");
                                    }
                                    else
                                    {
                                        _logger.Info("LazoSalidaEgreso -> BARRERA NO BAJA!! [{Name}] [{Name}]", EstaOcupadoBucleSalida() ? "(por bucle ocupado)" : "", GetVehiculo(eIndex).SalidaFalsa ? "(Por falsa salida)" : "");
                                    }
                                }
                                else
                                {
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                    _statusBarreraQ = false;
                                }
                            }
                        }
                        else
                        {
                            _logger.Info("LazoSalidaEgreso -> No hay vehiculos (si bucle libre)");
                            //No hay vehículos, bajo la barrera
                            if (!_statusBarreraQ)
                            {
                                if (!EstaOcupadoBucleSalida())
                                {
                                    if (!GetVehiculo(eIndex).BarreraAbierta)
                                    {
                                        DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                                        _logger.Info("LazoSalidaEgreso -> BARRERA ABAJO!!");
                                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                    }
                                    else
                                        _logger.Info("LazoSalidaEgreso -> Se mantiene la barrera arriba, se está asignando un tag hab 2");
                                }
                                else
                                {
                                    _logger.Info("LazoSalidaEgreso -> BARRERA NO BAJA!! (por bucle ocupado)");
                                }
                            }
                            else
                            {
                                _statusBarreraQ = false;
                            }
                        }
                    }

                    //List<DatoVia> listaDatosVia = new List<DatoVia>();
                    //ClassUtiles.InsertarDatoVia( GetVehiculo( eIndex ), ref listaDatosVia );
                    //ModuloPantalla.Instance.EnviarDatos( enmStatus.Ok, enmAccion.ACTUALIZA_ULTVEH, listaDatosVia );

                    //se envian los mimicos a pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    DecideCaptura(eCausaVideo.LazoSalidaDesocupado);
                    ActualizarEstadoSensores();
                    UpdateOnline();

                    //Al liberar la via sin otros vehiculos muestro mensaje
                    if (ViaSinVehPagados() && GetVehiculo(eVehiculo.eVehAnt).EstaPagado)
                    {
                        ModuloPantalla.Instance.LimpiarMensajes();
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("Ingrese la Categoría"));
                    }

                    // Se limpia posible alarma de Sip con error de loop
                    ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.SIPxDACFail, 0);

                    // Se envia mensaje a modulo de video continuo
                    ModuloVideoContinuo.Instance.CerrarCiclo();
                }

                //Si no está ocupado y hay un vehiculo categorizado
                if (!_enLazoSal && _logicaCobro.Estado == eEstadoVia.EVAbiertaCat)
                    _logicaCobro.LeerTarjeta();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            LoguearColaVehiculos();
            _logger.Info("******************* LazoSalidaEgreso -> Salida");
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

            respuesta = (sValor & ((int)eSensoresDAC.DEF_BPA_BYTE >> 4)) > 0 ? true : false;

            return respuesta;
        }

        /// <summary>
        /// Devuelve si el lazo de salida está ocupado. Usar esta desde lógica de cobro manual
        /// </summary>
        /// <returns></returns>
        override public bool EstaOcupadoLazoSalida()
        {
            return _enLazoSal;
        }

        /// <summary>
        /// Devuelve si el separador de salida está ocupado. Usar esta desde lógica de cobro manual
        /// </summary>
        /// <returns></returns>
        override public bool EstaOcupadoSeparadorSalida()
        {
            return _enSeparSal;
        }

        override public async void LazoEscapeIngreso()
        {
            //Entrada al lazo de Via de Escape
            LoguearColaVehiculos();
            _logger.Info("******************* LazoEscapeIngreso -> Ingreso");

            try
            {
                if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                {
                    Vehiculo veh = GetVehEscape();
                    CapturaVideoEscape(ref veh, eCausaVideo.ViaDeEscapeInicio);
                    CapturaFotoEscape(ref veh, eCausaVideo.ViaDeEscapeInicio);

                    if (_logicaCobro.EstadoEscape == eEstadoEscape.EVEPulsadorLibre)
                    {
                        _logicaCobro.EstadoEscape = eEstadoEscape.EVELazoOcupado;
                    }
                    else if (_logicaCobro.EstadoEscape == eEstadoEscape.EVELazoLibre || _logicaCobro.EstadoEscape == eEstadoEscape.EVEPulsadorPresionado)
                    {
                        _logicaCobro.EstadoEscape = eEstadoEscape.EVELazoOcupado;

                        // ConfiguracionAlarma para campana violacion
                        ConfigAlarma oCfgAlarma = await ModuloBaseDatos.Instance.BuscarConfiguracionAlarmaAsync("V");
                        if (oCfgAlarma != null)
                        {
                            DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, oCfgAlarma.DuracionVisual, oCfgAlarma.DuracionSonido, true);
                        }
                        else
                        {
                            //Por si falla la consulta
                            DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 1, 1, true);
                        }
                        IniciarTimerApagadoCampanaPantalla(2000);
                        // Actualiza el estado de los mimicos en pantalla
                        Mimicos mimicos = new Mimicos();
                        DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        mimicos.CampanaViolacion = enmEstadoAlarma.Activa;
                        ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                        ActualizarEstadoSensoresEscape();
                        ModuloEventos.Instance.SetEstadoOnline(true);
                    }
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* LazoEscapeIngreso -> Salida");
        }

        override public void LazoEscapeEgreso()
        {
            LoguearColaVehiculos();
            _logger.Info("******************* LazoEscapeEgreso -> Ingreso");

            try
            {
                if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                {
                    Vehiculo veh = GetVehEscape();
                    if (_logicaCobro.EstadoEscape == eEstadoEscape.EVELazoOcupado)
                    {
                        _logicaCobro.EstadoEscape = eEstadoEscape.EVEPulsadorLibre;

                        //Incremento el numero de transito de escape
                        veh.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransitoEscape);

                        CapturaVideoEscape(ref veh, eCausaVideo.ViaDeEscapeFin);
                        CapturaFotoEscape(ref veh, eCausaVideo.ViaDeEscapeFin);
                        DecideAlmacenar(eAlmacenaMedio.ViaEscape, ref veh);

                        //Cierro la barrera si estaba abierta
                        if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Escape))
                        {
                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Escape);
                            _logger.Info("LazoEscapeEgreso -> BARRERA ABAJO!!");
                            veh.TipoViolacion = 'B';  //Asignamos el tipo de violación
                        }
                        else
                            veh.TipoViolacion = 'R';  //Asignamos el tipo de violación

                        //Envio el evento de violacion
                        Turno turnoEscape = new Turno();
                        turnoEscape.EstadoTurno = enmEstadoTurno.Cerrada;
                        turnoEscape.Operador = new Operador();
                        turnoEscape.Parte = new Parte();

                        ModuloEventos.Instance.SetViolacionXML(ModuloBaseDatos.Instance.ConfigVia, turnoEscape, GetVehEscape(), true);
                    }
                    ModuloEventos.Instance.ActualizarVehiculoOnline(veh, true);
                    ActualizarEstadoSensoresEscape();
                    ModuloEventos.Instance.SetEstadoOnline(true);
                    //Limpio el vehiculo de escape
                    LimpiarVehEscape();
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* LazoEscapeEgreso -> Salida");
        }

        override public void PulsadorEscape(short sEstado, bool bFromSupervision)
        {
            int nviaescape = 0;
            //Se presiono el pulsador de apertura de barrera de Via de Escape
            _logger.Info($"PulsadorEscape -> Ingreso -> sEstado [{sEstado}] - bFromSupervision [{bFromSupervision}]");

            try
            {
                if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                {
                    nviaescape = ModuloBaseDatos.Instance.ConfigVia.NumeroViaEscape;
                    _logger.Info($"Estado Anterior Escape({nviaescape}): EstadoEscape [{_logicaCobro.EstadoEscape}]");
                    Vehiculo veh = GetVehEscape();
                    Turno turnoEscape = new Turno();
                    turnoEscape.EstadoTurno = enmEstadoTurno.Cerrada;
                    turnoEscape.Operador = new Operador();
                    turnoEscape.Parte = new Parte();
                    veh.Operacion = "VI";     //Tipo de Operación

                    switch (_logicaCobro.EstadoEscape)
                    {
                        //EV01
                        case eEstadoEscape.EVEPulsadorLibre:
                            //BARRERA ESCAPE ARRIBA
                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Escape);
                            _logger.Info("PulsadorEscape -> BARRERA ARRIBA!! [{0}]", eEstadoEscape.EVEPulsadorLibre.ToString());
                            _logicaCobro.EstadoEscape = eEstadoEscape.EVEPulsadorPresionado;

                            CapturaFotoEscape(ref veh, eCausaVideo.ViaDeEscapeInicio);
                            // Se envia el evento de Apertura de Barrera
                            ModuloEventos.Instance.SetAperturaBarrera(turnoEscape, eModoBarrera.Apertura, 0, true);
                            break;
                        //EV07
                        case eEstadoEscape.EVELazoOcupado:
                            //BARRERA ECAPE ARRIBA
                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Escape);
                            _logger.Info("PulsadorEscape -> BARRERA ARRIBA!! [{0}]", eEstadoEscape.EVELazoOcupado.ToString());
                            _logicaCobro.EstadoEscape = eEstadoEscape.EVEPulsadorPresionado;

                            CapturaFotoEscape(ref veh, eCausaVideo.ViaDeEscapeInicio);
                            LazoEscapeIngreso();
                            // Se envia el evento de Apertura de Barrera
                            ModuloEventos.Instance.SetAperturaBarrera(turnoEscape, eModoBarrera.Apertura, 0, true);
                            break;
                    }

                    ActualizarEstadoSensoresEscape();
                    ModuloEventos.Instance.SetEstadoOnline(true);
                    _logger.Info($"Estado Posterior Escape({nviaescape}): EstadoEscape [{_logicaCobro.EstadoEscape}]");
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            _logger.Info("PulsadorEscape -> Salida");
        }

        /// <summary>
        /// Se llama cuando se ocupa el BPI. Porque??????????????
        /// </summary>
        /// <param name="esModoForzado"></param>
        /// <param name="RespDAC"></param>
        override public void SeparadorSalidaIngreso(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();
            char respuestaDAC = ' ';

            try
            {
                _logger.Info("******************* SeparadorSalidaIngreso -> Ingreso[{Name}][{Name}]", respuestaDAC, RespDAC);

                // En el caso que no se haya liberado correctamente el lazo de salida
                //if (_enLazoSal && !EstaOcupadoBucleSalida())
                //    _enLazoSal = false;

                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                FallaSensoresDAC();

                ///////////////////////////////////////////////////////////////////////////////////////////////

                //Busco el vehículo más adelantado no vacío de atrás hacia adelante (eVehC0 a eVehP3)
                //utilizando eIndex-1 como inicio y si tiene tag habilitado levanto la barrera
                eVehiculo eIndex = eVehiculo.eVehC0;
                bool bEnc = false;
                while (eIndex >= eVehiculo.eVehP3 && !bEnc)
                {
                    bEnc = GetVehiculo(eIndex).NoVacio;
                    eIndex = (eVehiculo)((int)eIndex - 1);
                }

                //Me fijo si encontré un vehículo
                if (bEnc)
                {
                    //Encontré un vehículo (eIndex+1), si tiene tag habilitado abro la barrera
                    eIndex = (eVehiculo)((int)eIndex + 1);

                    if (GetVehiculo(eIndex).EstaPagado)
                    {
                        DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                        _logger.Info("SeparadorSalidaIngreso -> BARRERA ARRIBA!!");
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

                        // Actualiza el estado de los mimicos en pantalla
                        Mimicos mimicos = new Mimicos();
                        DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                    }
                }

                SetVehiculoIng();

                //Se usa para sacar la foto frontal (en vez de pagado)
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                {
                    DecideCaptura(eCausaVideo.LazoIntermedioOcupado, GetVehiculo(eIndex).NumeroVehiculo);
                }
            }

            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* SeparadorSalidaIngreso -> Salida");
        }

        override public void SeparadorSalidaEgreso(bool esModoForzado, short RespDAC)
        {
            bool bEnc, bTeniaFalsaSalida = false, bBarreraAbierta;
            eVehiculo eIndex;
            DateTime T1 = DateTime.Now;//m_Fecha;
            bool bSendLogica1 = false;
            bool bSendLogica2 = false;
            bool bSendLogica3 = false;
            byte btFiltrarSensores = 0;
            char respuestaDAC = ' ';
            _enSeparSal = false;

            LoguearColaVehiculos();
            _logger.Info("******************* SeparadorSalidaEgreso -> Ingreso[{Name}]", RespDAC);

            try
            {
                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                bBarreraAbierta = DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via);
                /************************************************/
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(false))
                {
                    if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera
                        && !DAC_PlacaIO.Instance.ExisteSensor(ConfiguracionDAC.EXISTE_SEP_SAL))
                    {
                        if (respuestaDAC == 'D' || respuestaDAC == '0')
                        {
                            eIndex = eVehiculo.eVehC0;
                            bEnc = false;
                            while (!bEnc && eIndex > eVehiculo.eVehP3)
                            {
                                bEnc = (GetVehiculo(eIndex).Init && GetVehiculo(eIndex).NoVacio);
                                if (!bEnc)
                                    eIndex = (eVehiculo)((int)eIndex - 1);
                            }
                            //Me fijo si encontré un vehículo
                            if (bEnc)
                            {
                                eIndex = (eVehiculo)((int)eIndex - 1);
                                //Me fijo si hay algun vehiculo atras que no esté pagado
                                //Pongo el semáforo de paso en Rojo por las dudas
                                //Si el vehiculo ingresado no tiene tag o no se le abrió la barrera
                                if (!GetVehiculo(eIndex).InfoTag.GetTagHabilitado() && !GetVehiculo(eIndex).BarreraAbierta)
                                {
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                    if (!EstaOcupadoBucleSalida())
                                    {
                                        DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                                        _logger.Info("SeparadorSalidaEgreso -> BARRERA ABAJO!!");
                                    }
                                    else
                                    {
                                        _logger.Info("SeparadorSalidaEgreso -> BARRERA NO BAJA por bucle ocupado");
                                    }
                                }
                            }
                        }
                    }
                }
                // Si no existe el separador no hay retroceso
                // Forzamos hacia adelante
                if (!DAC_PlacaIO.Instance.ExisteSensor(ConfiguracionDAC.EXISTE_SEP_SAL))
                    respuestaDAC = 'D';
                /************************************************/

                _logger.Info("SeparadorSalidaEgreso -> lRetDAC:[{Name}] cRetDAC:[{Name}]", RespDAC, respuestaDAC);

                // Limpio la autorizacion de paso
                _pendienteAutorizacionPaso = false;

                switch (respuestaDAC)
                {
                    case 'M': //Marcha atrás
                              //OBSOLETO se deja por compatibilidad con PICs viejos
                              //este mensaje ahora se recibe en Salida ON
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, marcha atrás (M)");
                        //Obtengo el vehículo ING sin considerar salida ON
                        //y reseteo el flag de salida ON
                        eIndex = GetVehiculoIng();
                        GetVehiculo(eIndex).SalidaON = false;
                        SetVehiculoIng(false, true);
                        break;

                    //TODO para F no generar la violacion en el siguiente
                    //Marcar al segundo como saliendo con falla y cuando termine de salir
                    //si no está pago generamos evento de chupado y no de violacionj
                    case 'F': //Adelante por falla
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, falla (F)");
                        //Busco el vehículo más adelantado
                        eIndex = eVehiculo.eVehC0;
                        bEnc = false;
                        while (!bEnc && eIndex >= eVehiculo.eVehP3)
                        {
                            bEnc = (GetVehiculo(eIndex).Init && GetVehiculo(eIndex).NoVacio);
                            if (!bEnc)
                                eIndex = (eVehiculo)((int)eIndex - 1);
                        }
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, falla (F) Busco vehículo más adelantado");
                        if (bEnc)
                        {
                            _logger.Info("SeparadorSalidaEgreso -> Modo D, Falla (F) encontré el vehículo T_Vehiculo:[{Name}]", eIndex.ToString());

                            //No generar falla por el BPI
                            btFiltrarSensores = (int)eSensoresDAC.DEF_BPI_BYTE;

                            //Marcamos falla en la salida para guardar 
                            GetVehiculo(eIndex).FallaSalida = true;
                            //Obtenemos la clasificacion del DAC por si realmente salio
                            if (GetVehiculo(eIndex).Fecha > DateTime.MinValue)
                                T1 = GetVehiculo(eIndex).Fecha;

                            Vehiculo oVehiculo = GetVehiculo(eIndex);
                            CategoriaPic(ref oVehiculo, T1, false, null);
                            _ultimoDac = oVehiculo.InfoDac;

                            //Busco el vehículo más adelantado no vacío de atrás hacia adelante (eVehC0 a eVehP3)
                            //utilizando eIndex-1 como inicio y marco su salida chupado
                            eIndex = (eVehiculo)((int)eIndex - 1);
                            bEnc = false;
                            while ((int)eIndex >= (int)eVehiculo.eVehP3 && !bEnc)
                            {
                                bEnc = GetVehiculo(eIndex).NoVacio;
                                eIndex = (eVehiculo)((int)eIndex - 1);
                            }

                            // Se envia mensaje a modulo de video continuo
                            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.Detectado, null, null, GetVehiculo(eIndex));

                            //Me fijo si encontré un vehículo
                            if (bEnc)
                            {
                                //Encontré un vehículo (eIndex+1), lo marco como chupado
                                eIndex = (eVehiculo)((int)eIndex + 1);
                                _logger.Info("SeparadorSalidaEgreso -> Hay 1 vehículo ocupado. T_Vehiculo:[{Name}]", eIndex.ToString());


                                //Marcamos como chupado
                                _logger.Info("SeparadorSalidaEgreso->Vehiculo de atras Marcamos Chupado");
                                GetVehiculo(eIndex).SalioChupado = true;
                            }
                        }
                        break;
                    case 'D': //Adelante
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, adelante (D)");
                        //Busco el vehículo más adelantado
                        eIndex = eVehiculo.eVehC0;
                        bEnc = false;
                        while (!bEnc && eIndex >= eVehiculo.eVehP3)
                        {
                            bEnc = (GetVehiculo(eIndex).Init && GetVehiculo(eIndex).NoVacio);
                            if (!bEnc)
                                eIndex = (eVehiculo)((int)eIndex - 1);
                        }
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, adelante (D) Busco vehículo más adelantado");
                        if (bEnc)
                        {
                            _logger.Info("SeparadorSalidaEgreso -> Modo D, adelante (D) encontré el vehículo T_Vehiculo:[{Name}] Siguiente [{Name}] Estado [{Name}] Barrera [{Name}]", eIndex.ToString(), GetVehiculo(eIndex - 1).Ocupado ? "OCUPADO" : "LIBRE", _logicaCobro.Estado.ToString(), bBarreraAbierta ? "ABIERTA" : "CERRADA");


                            //Si la barrera está cerrada y tengo otro vehiculo más es una falsa salida
                            if ((eIndex == eVehiculo.eVehC0 || eIndex == eVehiculo.eVehC1)
                                && _logicaCobro.Estado != eEstadoVia.EVCerrada
                                && !bBarreraAbierta
                                && GetVehiculo(eIndex - 1).Ocupado)
                            {
                                //Marcamos como false salida
                                _logger.Info("SeparadorSalidaEgreso->Falsa salida del vehiculo [{Name}]!!! No lo sacamos.", eIndex.ToString());
                                GetVehiculo(eIndex).SalidaFalsa = true;
                            }
                            else
                            {
                                _logger.Debug("SeparadorSalidaEgreso -> Vehiculo {0} Tiene salida Falsa? {1}", eIndex.ToString(), GetVehiculo(eIndex).SalidaFalsa ? "SI" : "NO");
                                bTeniaFalsaSalida = GetVehiculo(eIndex).SalidaFalsa;

                                //Si salio chupado le copio el video
                                if (GetVehiculo(eIndex).SalioChupado)
                                {
                                    _logger.Info("SeparadorSalidaEgreso->Chupado copiamos el video anterior");
                                    GetVehiculo(eIndex).ListaInfoVideo = _ultimoVideo;
                                    if (_ultimoDac.YaTieneDAC)
                                        GetVehiculo(eIndex).InfoDac = _ultimoDac;
                                }
                                else
                                {
                                    //Salvo video para el chupado
                                    _ultimoVideo = GetVehiculo(eIndex).ListaInfoVideo;
                                    if (GetVehiculo(eIndex).InfoDac.YaTieneDAC)
                                        _ultimoDac = GetVehiculo(eIndex).InfoDac;
                                }

                                GenerarEventoSiOcupado(eIndex);
                                //Si eIndex no es C0 ni C1 genero un evento de falla crítica y log de sensores
                                if (eIndex != eVehiculo.eVehC0 && eIndex != eVehiculo.eVehC1)
                                {
                                    if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    {
                                        _logger.Info("SeparadorSalidaEgreso -> Modo D, adelante (D) No era C0 ni C1, logSensores");
                                        bSendLogica1 = true;
                                    }
                                }

                                if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                                {
                                    //Busco el vehículo más adelantado no vacío de atrás hacia adelante (eVehC0 a eVehP3)
                                    //utilizando eIndex-1 como inicio y si tiene tag habilitado levanto la barrera
                                    eIndex = (eVehiculo)((int)eIndex - 1);
                                    bEnc = false;
                                    while (eIndex >= eVehiculo.eVehP3 && !bEnc)
                                    {
                                        bEnc = GetVehiculo(eIndex).NoVacio;
                                        eIndex = (eVehiculo)((int)eIndex - 1);
                                    }

                                    //Me fijo si encontré un vehículo
                                    if (bEnc)
                                    {
                                        //Encontré un vehículo (eIndex+1), si tiene tag habilitado abro la barrera
                                        eIndex = (eVehiculo)((int)eIndex + 1);
                                        _logger.Info("SeparadorSalidaEgreso -> Hay 1 vehículo ocupado. T_Vehiculo:[{Name}]", eIndex.ToString());

                                        //Si el siguiente salio chupado, lo quitamos
                                        if (GetVehiculo(eIndex).SalioChupado)
                                        {
                                            _logger.Info("SeparadorSalidaEgreso->Vehiculo de atras Salio Chupado");
                                            //Le copiamos el video del anterior
                                            _logger.Info("SeparadorSalidaEgreso -> Chupado Copiamos el video del anterior");
                                            GetVehiculo(eIndex).ListaInfoVideo = _ultimoVideo;
                                            if (_ultimoDac.YaTieneDAC)
                                            {
                                                _logger.Info("SeparadorSalidaEgreso -> Chupado Copiamos el CatDAC del anterior");
                                                GetVehiculo(eIndex).InfoDac = _ultimoDac;
                                            }
                                            GenerarEventoSiOcupado(eIndex);
                                        }
                                        //Si el anterior tenia falsa salida, lo quitamos
                                        if (bTeniaFalsaSalida)
                                        {
                                            _logger.Info("SeparadorSalidaEgreso->Vehiculo anterior con falsa salida");
                                            GenerarEventoSiOcupado(eIndex);
                                        }


                                        if (GetVehiculo(eIndex).EstaPagado)
                                        {
                                            _logger.Info("SeparadorSalidaEgreso -> Subir barrera, tag habilitado. T_Vehiculo:[{Name}]", eIndex.ToString());

                                            //Elimino del vehiculo las fotos de la lectura del tag
                                            GetVehiculo(eIndex).ListaInfoFoto.RemoveAll(x => x != null && (x.Causa == eCausaVideo.TagLeidoPorAntena || x.Causa == eCausaVideo.PagadoTelepeaje));
                                            //Saco una foto del momento en el que llega el tag por antena y la agrego a InfoMedios
                                            InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, eCausaVideo.PagadoTelepeaje);
                                            ModuloFoto.Instance.SacarFoto(new Vehiculo(), eCausaVideo.PagadoTelepeaje, false, oFoto);
                                            GetVehiculo(eIndex).ListaInfoFoto.Add(oFoto);

                                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                                            _logger.Info("SeparadorSalidaEgreso -> BARRERA ARRIBA!!");
                                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

                                            // Actualiza el estado de los mimicos en pantalla
                                            Mimicos mimicos = new Mimicos();
                                            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                                            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                                        }
                                        else if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera && GetVehiculo(eIndex).Categoria > 0)
                                        {
                                            _logicaCobro.Estado = eEstadoVia.EVAbiertaCat;
                                        }
                                        else if (_logicaCobro.Estado != eEstadoVia.EVQuiebreBarrera)
                                            _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;

                                    }
                                }
                            }
                        }
                        else
                        {
                            //La via estaba vacia
                            //Error de Logica
                            if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                            {
                                _logger.Info("SeparadorSalidaEgreso -> Modo D, adelante (D) con Vía Vacía, logSensores");
                                bSendLogica2 = true;
                            }

                            //Ocupo C0 y genero una violacion
                            GetVehiculo(eVehiculo.eVehC0).Ocupado = true;
                            GenerarEventoSiOcupado(eVehiculo.eVehC0);

                        }
                        if (_logicaCobro.Estado == eEstadoVia.EVAbiertaLibre)
                            Autotabular();
                        SetVehiculoIng(false, true);

                        break;

                    case '0': //Vaciar la vía
                        _logger.Info("SeparadorSalidaEgreso -> Modo D, vaciar la vía (0)");

                        //Si salio chupado le copio el video
                        if (GetVehiculo(eVehiculo.eVehC0).SalioChupado && _ultimoVideo.Any(x => x != null))
                        {
                            if (!GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo.Any(x => x != null))
                            {
                                _logger.Info("SeparadorSalidaEgreso->Chupado copiamos el video anterior por C0");
                                GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo = _ultimoVideo;
                            }
                        }
                        if (GetVehiculo(eVehiculo.eVehC1).SalioChupado)
                        {
                            _ultimoVideo = GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo;
                            if (!GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo.Any(x => x != null) && _ultimoVideo.Any(x => x != null))
                            {
                                _logger.Info("SeparadorSalidaEgreso->Chupado copiamos el video anterior por C1");
                                GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo = _ultimoVideo;
                            }
                        }
                        //Estado normal 1 solo vehiculo ocupado en la cola (puede haber alguno en la precola)
                        ////Estado normal C1 ocupado y C0 libre. 
                        // o C0 y C1 ocupados si C1 salio chupado
                        //Si no es este estado genero un evento de falla crítica y log de sensores
                        if (GetVehiculo(eVehiculo.eVehC1).Ocupado && GetVehiculo(eVehiculo.eVehC0).Ocupado)
                        {
                            if (GetVehiculo(eVehiculo.eVehC1).SalioChupado)
                            {
                                //Copiamos el video de C0 a C1
                                _logger.Info("SeparadorSalidaEgreso -> Chupado Copiamos el video de C0 a C1");
                                _ultimoVideo = GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo;
                                GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo = _ultimoVideo;
                                if (GetVehiculo(eVehiculo.eVehC0).InfoDac.YaTieneDAC)
                                {
                                    _logger.Info("SeparadorSalidaEgreso -> Chupado Copiamos el CatDAC de C0 a C1");
                                    _ultimoDac = GetVehiculo(eVehiculo.eVehC0).InfoDac;
                                    GetVehiculo(eVehiculo.eVehC1).InfoDac = _ultimoDac;
                                }
                            }
                            else
                            {
                                //si no tiene video copiamos el de C0
                                if (GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo == null || GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo.Count == 0)
                                {
                                    _logger.Info("SeparadorSalidaEgreso -> Copiamos el video de C0 a C1 por falta de video en C1");
                                    _ultimoVideo = GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo;
                                    GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo = _ultimoVideo;
                                }

                                if (!GetVehiculo(eVehiculo.eVehC1).InfoDac.YaTieneDAC)
                                {
                                    _logger.Info("SeparadorSalidaEgreso -> Copiamos el CatDAC de C0 a C1 porque C1 no tiene");
                                    _ultimoDac = GetVehiculo(eVehiculo.eVehC0).InfoDac;
                                    GetVehiculo(eVehiculo.eVehC1).InfoDac = _ultimoDac;
                                }

                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    bSendLogica3 = true;
                            }
                        }
                        else if (!(GetVehiculo(eVehiculo.eVehC1).Ocupado && !GetVehiculo(eVehiculo.eVehC0).Ocupado))
                        {
                            if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                bSendLogica3 = true;
                        }

                        // Para el caso que haya que vaciar la via si no hay ningun vehiculo en C0
                        if (!GetVehiculo(eVehiculo.eVehC0).Ocupado && GetVehiculo(eVehiculo.eVehC1).Ocupado && GetVehiculo(eVehiculo.eVehP1).Ocupado)
                        {
                            _ultimoVideo = GetVehiculo(eVehiculo.eVehC1).ListaInfoVideo;
                        }


                        //Genero un evento si C0 está ocupado
                        GenerarEventoSiOcupado(eVehiculo.eVehC0);

                        if (GetVehiculo(eVehiculo.eVehC0).InfoDac.YaTieneDAC)
                        {
                            _ultimoDac = GetVehiculo(eVehiculo.eVehC0).InfoDac;
                            if (!GetVehiculo(eVehiculo.eVehC1).InfoDac.YaTieneDAC && GetVehiculo(eVehiculo.eVehC1).NoVacio)
                                GetVehiculo(eVehiculo.eVehC1).InfoDac = _ultimoDac;
                        }

                        //Si salio chupado ya lo estamos vaciando
                        //Genero un evento si C1 está ocupado
                        GenerarEventoSiOcupado(eVehiculo.eVehC1);


                        ////////////////////////////////////////////////////////////////////////////////////////////////////
                        //Busco vehiculo en la precola al vehiculo mas adelantado no vacio 
                        eIndex = eVehiculo.eVehP1;
                        bEnc = false;
                        while (!bEnc && eIndex >= eVehiculo.eVehP3)
                        {
                            bEnc = (GetVehiculo(eIndex).Init && GetVehiculo(eIndex).NoVacio);

                            if (!bEnc)
                                eIndex = (eVehiculo)((int)eIndex - 1);
                        }

                        if (bEnc)
                        {
                            // Si esta categorizado, pero no pagado ni ocupado. No genero el evento
                            if (GetVehiculo(eIndex).Categoria > 0 && !GetVehiculo(eIndex).Ocupado && !GetVehiculo(eIndex).EstaPagado)
                                bEnc = false;
                        }

                        _logger.Info("SeparadorSalidaEgreso -> Modo D, VaciarVia (0) Busco vehículo más adelantado");

                        if (bEnc)
                        {
                            _logger.Info("SeparadorSalidaEgreso -> Modo D, VaciarVia (0) encontré el vehículo T_Vehiculo:[{Name}] Siguiente [{Name}] Estado [{Name}] Barrera [{Name}]", eIndex.ToString(), GetVehiculo(eIndex - 1).Ocupado ? "OCUPADO" : "LIBRE", _logicaCobro.Estado.ToString(), bBarreraAbierta ? "ABIERTA" : "CERRADA");

                            bTeniaFalsaSalida = GetVehiculo(eIndex).SalidaFalsa;

                            //Si salio chupado le copio el video
                            if (GetVehiculo(eIndex).SalioChupado || GetVehiculo(eIndex).Ocupado)
                            {
                                _logger.Info("SeparadorSalidaEgreso->Modo D, VaciarVia (0), Chupado copiamos el video anterior");
                                GetVehiculo(eIndex).ListaInfoVideo = _ultimoVideo;
                                GetVehiculo(eIndex).InfoDac = _ultimoDac;
                            }
                            else
                            {
                                //Salvo video para el chupado
                                _ultimoVideo = GetVehiculo(eIndex).ListaInfoVideo;
                            }

                            GetVehiculo(eIndex).Ocupado = false;
                            GetVehiculo(eIndex).NumeroVehiculo = 0;

                            if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                bSendLogica1 = true;
                        }
                        ////////////////////////////////////////////////////////////////////////////////////////////////////

                        //La precola solo la vacío si en este momento el separador de entrada está libre
                        if (!_bEnLazoPresencia)
                        {
                            //Si P3 está ocupado y tiene forma de pago válida genero un evento de Op. cerrada
                            //por marcha atrás
                            OpCerradaReversa(eVehiculo.eVehP3);
                            //Idem P2
                            OpCerradaReversa(eVehiculo.eVehP2);
                            //SI esta tabulado, no esta pagado, y no tiene tag
                            //guardamos la categoria
                            bool Categorizar = false;
                            short catego = 0;
                            if (GetVehiculo(eVehiculo.eVehP1).Categoria > 0 && !GetVehiculo(eVehiculo.eVehP1).EstaPagado
                                && !GetVehiculo(eVehiculo.eVehP1).InfoTag.GetTagHabilitado())
                            {
                                Categorizar = true;
                                catego = GetVehiculo(eVehiculo.eVehP1).Categoria;

                            }
                            //Idem P1
                            OpCerradaReversa(eVehiculo.eVehP1);
                            //SI guardamos la categoria, se la volvemos a asignar
                            if (Categorizar)
                            {
                                _logicaCobro.Categorizar(catego);
                            }
                        }

                        //Asigno el vehículo ing de acuerdo a lo que quedo
                        SetVehiculoIng(false, true);

                        break;
                }

                //Este flag no debe quedar puesto si el separador está libre
                _flagIniViol = false;

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
                    FallaCritica oFallaCritica = new FallaCritica();
                    oFallaCritica.CodFallaCritica = EnmFallaCritica.FCPic;
                    oFallaCritica.Observacion = "Error en el DAC";

                    ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, null);
                }

                if (respuestaDAC != 'M')
                    FecUltimoTransito = DateTime.Now;//m_Fecha;

                GrabarVehiculos();

                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.Simulacion, 0);

                // Se envia mensaje a modulo de video continuo
                if (_logicaCobro.Estado != eEstadoVia.EVCerrada || (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true)))
                    ModuloVideoContinuo.Instance.CerrarCiclo();

                RegularizarFilaVehiculos();

                //limpiamos vehiculo a eliminar
                _vehiculoAEliminar.Clear();

                //Si hay un tag cancelado lo eliminamos de la lista de la antena para poder leerlo nuevamente
                if (!string.IsNullOrEmpty(_ultTagCancelado.NumeroTag))
                {
                    ModuloAntena.Instance.BorrarTag(_ultTagCancelado.NumeroTag);
                    SetMismoTagIpico(_ultTagCancelado.NumeroTag);
                    _ultTagCancelado.Clear();
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* SeparadorSalidaEgreso -> Salida");
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
                    if (oVehMD.NumeroTransito == 0)
                        oVehMD.NumeroTransito = IncrementoTransito();

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

        /// <summary>
        /// Persiste los datos de vehiculos en la base de datos
        /// </summary>
        public override void GrabarVehiculos()
        {
            //ModuloBaseDatos.Instance.GrabarVarsAsync(_vVehiculo);
        }


        override public void LazoPresenciaIngreso(bool esModoForzado, short RespDAC)
        {
            char respuestaDAC = ' ';

            _logger.Info("******************* LazoPresenciaIngreso -> Ingreso[{Name}]", RespDAC);
            LoguearColaVehiculos();
            try
            {
                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                _bEnLazoPresencia = true;
                ActivarAntena(respuestaDAC, eCausaLecturaTag.eCausaLazoPresencia);

                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                DecideCaptura(eCausaVideo.LazoPresenciaOcupado, _ultVehiculoAntena);
                GrabarVehiculos();

                ActualizarEstadoSensores();
                UpdateOnline();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            LoguearColaVehiculos();
            _logger.Info("******************* LazoPresenciaIngreso -> Salida");
        }

        private object _lockDecideCaptura = new object();

        /// <summary>
        /// Selecciona el vehiculo correspondiente al sensor indicado
        /// Y llama a la captura de video y foto, para revisar la configuración necesaria
        /// </summary>
        /// <param name="sensor"></param>
        public override void DecideCaptura(eCausaVideo sensor, ulong NumeroVehiculo = 0)
        {
            lock (_lockDecideCaptura)
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

                _logger.Info("DecideCaptura -> Sensor [{name}] NumeroVehiculo [{name}] TipOp [{name}] TipBo [{name}] FP [{name}]", sensor.ToString(), oVehiculo?.NumeroVehiculo, oVehiculo?.TipOp, oVehiculo?.TipBo, oVehiculo?.FormaPago.ToString());
                CapturaVideo(ref oVehiculo, ref sensor);
                CapturaFoto(ref oVehiculo, ref sensor);

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

        //Captura de foto y video via de escape
        public override void CapturaFotoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor)
        {
            if (oVehiculo != null)
            {
                if (sensor == eCausaVideo.ViaDeEscapeInicio || sensor == eCausaVideo.ViaDeEscapeFin)
                {
                    InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Escape), eCamara.Escape, eTipoMedio.Foto, sensor, false);

                    oVehiculo.ListaInfoFoto?.Add(oFoto);
                    _logger.Trace("LogicaViaDinamica::CapturaFotoEscape -> Saco foto");

                    ModuloFoto.Instance.SacarFoto(oVehiculo, sensor, false, oFoto);
                }
            }
        }

        override public void CapturaVideoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor)
        {
            if (oVehiculo != null)
            {
                if (sensor == eCausaVideo.ViaDeEscapeInicio)
                {
                    bool containsItem = false;
                    if (oVehiculo.ListaInfoVideo.Count != 0)
                        containsItem = oVehiculo.ListaInfoVideo.Any(item => item.EstaFilmando == true);
                    if (!containsItem)
                    {
                        InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Escape), eCamara.Escape, eTipoMedio.Video, sensor);
                        oVideo.EstaFilmando = true;
                        oVehiculo.ListaInfoVideo?.Add(oVideo);

                        ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                    }
                }
                else if (sensor == eCausaVideo.ViaDeEscapeFin)
                {
                    ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Escape);
                    int index = oVehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
                    if (index != -1)
                        oVehiculo.ListaInfoVideo[index].EstaFilmando = false;
                }
            }
        }

        /// <summary>
        /// Revisa si es necesario iniciar o detener un video para 
        /// </summary>
        /// <param name="oVehiculo"></param>
        override public void CapturaVideo(ref Vehiculo oVehiculo, ref eCausaVideo sensor, bool esManual = false)
        {
            try
            {
                if (oVehiculo != null)
                {
                    // Inicio video
                    bool iniciarVideo = false, finVideo = false, iniciarVideo2 = false, finVideo2 = false;
                    bool NoIniciarVideo = false;

                    VideoAcc oVideoAcc = ModuloBaseDatos.Instance.ObtenerVideoAcc((int)sensor);

                    if (!string.IsNullOrEmpty(oVideoAcc.Accion))
                    {
                        // CAMARA LATERAL
                        iniciarVideo = oVideoAcc.ComienzaGrabacion == "S" &&
                                       (ModuloBaseDatos.Instance.ConfigVia.Video == "V" || ModuloBaseDatos.Instance.ConfigVia.Video == "S"); // Chequear si este es para video siempre

                        if (iniciarVideo)
                        {
                            bool containsItem = false;

                            if (oVehiculo.ListaInfoVideo.Count != 0)
                                containsItem = oVehiculo.ListaInfoVideo.Any(item => item != null /*&& item.EstaFilmando == true*/ && item.Camara == eCamara.Lateral);

                            eCausaVideo causaVideoAux = sensor;

                            // Si la causa para iniciar video es por un pagado, descarto cualquier video anterior por lazos
                            if (containsItem)
                            {
                                int index = 0;
                                if (causaVideoAux == eCausaVideo.Pagado || causaVideoAux == eCausaVideo.PagadoExento || causaVideoAux == eCausaVideo.PagadoTelepeaje)
                                {
                                    // Si se estaba filmando un video, lo detengo
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Lateral);

                                    if (index != -1)
                                    {
                                        InfoMedios infoMediosAux = oVehiculo.ListaInfoVideo[index];
                                        ModuloVideo.Instance.DetenerVideo(oVehiculo, infoMediosAux.Causa, eCamara.Lateral);
                                    }

                                    // Descarto los videos anteriores realizados una causa de lazo de entrada
                                    oVehiculo.ListaInfoVideo.RemoveAll(x => x != null && x.Camara == eCamara.Lateral && (x.Causa == eCausaVideo.LazoPresenciaOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoPresenciaDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.Violacion));
                                    index = 0;
                                }
                                else if (causaVideoAux == eCausaVideo.LazoPresenciaOcupado || causaVideoAux == eCausaVideo.LazoPresenciaDesocupado || causaVideoAux == eCausaVideo.LazoIntermedioOcupado
                                        || causaVideoAux == eCausaVideo.LazoIntermedioDesocupado)
                                {
                                    if (oVehiculo.ListaInfoVideo.Any(x => x != null && (x.Causa == eCausaVideo.Pagado || x.Causa == eCausaVideo.PagadoExento || x.Causa == eCausaVideo.PagadoTelepeaje)))
                                        NoIniciarVideo = true;
                                }

                                if (!NoIniciarVideo)
                                {
                                    // Busco si ya tenia un video con esa causa
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.Camara == eCamara.Lateral && item.Causa == causaVideoAux);

                                    // Inicia captura de video
                                    InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Lateral), eCamara.Lateral, eTipoMedio.Video, sensor);
                                    if (oVideo != null)
                                    {
                                        oVideo.EstaFilmando = true;

                                        // Si ya tenia un video con esa causa, lo piso
                                        if (index != -1)
                                            oVehiculo.ListaInfoVideo[index] = oVideo;
                                        else
                                            oVehiculo.ListaInfoVideo?.Add(oVideo);

                                        ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                    }
                                }
                            }
                            else
                            {
                                // No existia un video con la camara lateral
                                InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Lateral), eCamara.Lateral, eTipoMedio.Video, sensor);
                                if (oVideo != null)
                                {
                                    oVideo.EstaFilmando = true;
                                    oVehiculo.ListaInfoVideo?.Add(oVideo);
                                    ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                }
                            }
                        }

                        finVideo = oVideoAcc.TerminaGrabacion == "S"; // Chequear si este es para video siempre

                        if (finVideo)
                        {
                            ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Lateral);

                            int index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Lateral);

                            if (index != -1)
                                oVehiculo.ListaInfoVideo[index].EstaFilmando = false;
                        }


                        // CAMARA INTERNA
                        iniciarVideo = oVideoAcc.ComienzaGrabacion == "S" &&
                                       (ModuloBaseDatos.Instance.ConfigVia.Video4 == "V" || ModuloBaseDatos.Instance.ConfigVia.Video4 == "S"); // Chequear si este es para video siempre

                        if (iniciarVideo)
                        {
                            bool containsItem = false;

                            if (oVehiculo.ListaInfoVideo.Count != 0)
                                containsItem = oVehiculo.ListaInfoVideo.Any(item => item != null /*&& item.EstaFilmando == true*/ && item.Camara == eCamara.Interna);

                            eCausaVideo causaVideoAux = sensor;

                            // Si la causa para iniciar video es por un pagado, descarto cualquier video anterior por lazos
                            if (containsItem)
                            {
                                int index = 0;
                                if (causaVideoAux == eCausaVideo.Pagado || causaVideoAux == eCausaVideo.PagadoExento || causaVideoAux == eCausaVideo.PagadoTelepeaje)
                                {
                                    // Si se estaba filmando un video, lo detengo
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Interna);

                                    if (index != -1)
                                    {
                                        InfoMedios infoMediosAux = oVehiculo.ListaInfoVideo[index];
                                        ModuloVideo.Instance.DetenerVideo(oVehiculo, infoMediosAux.Causa, eCamara.Interna);
                                    }

                                    // Descarto los videos anteriores realizados una causa de lazo de entrada
                                    oVehiculo.ListaInfoVideo.RemoveAll(x => x != null && x.Camara == eCamara.Interna && (x.Causa == eCausaVideo.LazoPresenciaOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoPresenciaDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.Violacion));
                                    index = 0;
                                }
                                else if (causaVideoAux == eCausaVideo.LazoPresenciaOcupado || causaVideoAux == eCausaVideo.LazoPresenciaDesocupado || causaVideoAux == eCausaVideo.LazoIntermedioOcupado
                                        || causaVideoAux == eCausaVideo.LazoIntermedioDesocupado)
                                {
                                    if (oVehiculo.ListaInfoVideo.Any(x => x != null && (x.Causa == eCausaVideo.Pagado || x.Causa == eCausaVideo.PagadoExento || x.Causa == eCausaVideo.PagadoTelepeaje)))
                                        NoIniciarVideo = true;
                                }

                                if (!NoIniciarVideo)
                                {
                                    // Busco si ya tenia un video con esa causa
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.Camara == eCamara.Interna && item.Causa == causaVideoAux);

                                    // Inicia captura de video
                                    InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Interna), eCamara.Interna, eTipoMedio.Video, sensor);
                                    if (oVideo != null)
                                    {
                                        oVideo.EstaFilmando = true;

                                        // Si ya tenia un video con esa causa, lo piso
                                        if (index != -1)
                                            oVehiculo.ListaInfoVideo[index] = oVideo;
                                        else
                                            oVehiculo.ListaInfoVideo?.Add(oVideo);

                                        ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                    }
                                }
                            }
                            else
                            {
                                // No existia un video con la camara lateral
                                InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Interna), eCamara.Interna, eTipoMedio.Video, sensor);
                                if (oVideo != null)
                                {
                                    oVideo.EstaFilmando = true;
                                    oVehiculo.ListaInfoVideo?.Add(oVideo);
                                    ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                }
                            }
                        }

                        finVideo = oVideoAcc.TerminaGrabacion == "S"; // Chequear si este es para video siempre

                        if (finVideo)
                        {
                            ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Interna);

                            int index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Interna);

                            if (index != -1)
                                oVehiculo.ListaInfoVideo[index].EstaFilmando = false;
                        }


                        // CAMARA FRONTAL
                        iniciarVideo2 = oVideoAcc.ComienzaGrabacion2 == "S" &&
                                        (ModuloBaseDatos.Instance.ConfigVia.Video2 == "V" || ModuloBaseDatos.Instance.ConfigVia.Video2 == "S"); // Chequear si este es para video siempre

                        if (iniciarVideo2)
                        {
                            bool containsItem = false;

                            if (oVehiculo.ListaInfoVideo.Count != 0)
                                containsItem = oVehiculo.ListaInfoVideo.Any(item => item != null /*&& item.EstaFilmando == true*/ && item.Camara == eCamara.Frontal);

                            eCausaVideo causaVideoAux = sensor;

                            int index = 0;
                            NoIniciarVideo = false;

                            // Si la causa para iniciar video es por un pagado, descarto cualquier video anterior por lazos
                            if (containsItem)
                            {
                                if (causaVideoAux == eCausaVideo.Pagado || causaVideoAux == eCausaVideo.PagadoExento || causaVideoAux == eCausaVideo.PagadoTelepeaje)
                                {
                                    // Si se estaba filmando un video, lo detengo
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Frontal);

                                    if (index != -1)
                                    {
                                        InfoMedios infoMediosAux = oVehiculo.ListaInfoVideo[index];
                                        ModuloVideo.Instance.DetenerVideo(oVehiculo, infoMediosAux.Causa, eCamara.Frontal);
                                    }

                                    // Descarto los videos anteriores realizados una causa de lazo de entrada
                                    oVehiculo.ListaInfoVideo.RemoveAll(x => x != null && x.Camara == eCamara.Frontal && (x.Causa == eCausaVideo.LazoPresenciaOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoPresenciaDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioOcupado ||
                                                                                                                          x.Causa == eCausaVideo.LazoIntermedioDesocupado ||
                                                                                                                          x.Causa == eCausaVideo.Violacion));
                                }
                                else if (causaVideoAux == eCausaVideo.LazoPresenciaOcupado || causaVideoAux == eCausaVideo.LazoPresenciaDesocupado || causaVideoAux == eCausaVideo.LazoIntermedioOcupado
                                        || causaVideoAux == eCausaVideo.LazoIntermedioDesocupado)
                                {
                                    if (oVehiculo.ListaInfoVideo.Any(x => x != null && (x.Causa == eCausaVideo.Pagado || x.Causa == eCausaVideo.PagadoExento || x.Causa == eCausaVideo.PagadoTelepeaje)))
                                        NoIniciarVideo = true;
                                }

                                if (!NoIniciarVideo)
                                {
                                    // Busco si ya tenia un video con esa causa
                                    index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.Camara == eCamara.Frontal && item.Causa == causaVideoAux);

                                    // Inicia captura de video
                                    InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Video, sensor);
                                    if (oVideo != null)
                                    {
                                        oVideo.EstaFilmando = true;

                                        // Si ya tenia un video con esa causa, lo piso
                                        if (index != -1)
                                            oVehiculo.ListaInfoVideo[index] = oVideo;
                                        else
                                            oVehiculo.ListaInfoVideo?.Add(oVideo);

                                        ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                    }
                                }
                            }
                            else
                            {
                                // No existia un video con la camara frontal
                                InfoMedios oVideo = new InfoMedios(ObtenerNombreVideo(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Video, sensor);
                                if (oVideo != null)
                                {
                                    oVideo.EstaFilmando = true;
                                    oVehiculo.ListaInfoVideo?.Add(oVideo);
                                    ModuloVideo.Instance.IniciarVideo(oVehiculo, sensor, oVideo);
                                }
                            }
                        }

                        finVideo2 = oVideoAcc.TerminaGrabacion2 == "S"; // Chequear si este es para video siempre
                        if (finVideo2)
                        {
                            ModuloVideo.Instance.DetenerVideo(oVehiculo, sensor, eCamara.Frontal);

                            int index = oVehiculo.ListaInfoVideo.FindIndex(item => item != null && item.EstaFilmando == true && item.Camara == eCamara.Frontal);

                            if (index != -1)
                                oVehiculo.ListaInfoVideo[index].EstaFilmando = false;
                        }
                    }
                    else
                        _logger.Trace("No se encontro la accion en la base de datos ");
                }
                else
                    _logger.Info("CapturaVideo -> Vehiculo en null");
            }
            catch (Exception e)
            {

                _loggerExcepciones?.Error(e);
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
                if (oVehiculo != null)
                {
                    bool sacarFoto = false;
                    VideoAcc oVideoAcc = ModuloBaseDatos.Instance.ObtenerVideoAcc((int)sensor);

                    if (!string.IsNullOrEmpty(oVideoAcc.Accion) || sensor == eCausaVideo.Manual)
                    {
                        // CAMARA FRONTAL
                        sacarFoto = oVideoAcc.ComienzaGrabacion2 == "S" &&
                                    (ModuloBaseDatos.Instance.ConfigVia.Video2 == "F" || ModuloBaseDatos.Instance.ConfigVia.Video2 == "S"); // Chequear si este es para foto siempre

                        if (sensor == eCausaVideo.Manual || sacarFoto)
                        {
                            InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, sensor, esManual);
                            if (oFoto != null)
                            {
                                // Si la foto no es de tipo manual, se agrega a la lista de medios del vehiculo
                                if (!esManual)
                                {
                                    if (oVehiculo.ListaInfoFoto == null)
                                        oVehiculo.ListaInfoFoto = new List<InfoMedios>();

                                    if (!oVehiculo.ListaInfoFoto.Any(x => x != null && x.NombreMedio == oFoto.NombreMedio))
                                    {
                                        eCausaVideo causaAux = sensor;

                                        if (oVehiculo.ListaInfoFoto.Any(x => x != null && x.Camara == eCamara.Frontal && x.Causa == causaAux))
                                        {
                                            oVehiculo.ListaInfoFoto[oVehiculo.ListaInfoFoto.FindIndex(x => x != null && x.Camara == eCamara.Frontal && x.Causa == causaAux)] = oFoto;
                                        }
                                        // Si se debe tomar una foto por pagado, se descartan las fotos tomadas por lazos
                                        else if (causaAux == eCausaVideo.Pagado || causaAux == eCausaVideo.PagadoExento || causaAux == eCausaVideo.PagadoTelepeaje)
                                        {
                                            oVehiculo.ListaInfoFoto.RemoveAll(x => x != null && x.Camara == eCamara.Frontal && (x.Causa == eCausaVideo.LazoPresenciaOcupado ||
                                                                                                                                 x.Causa == eCausaVideo.LazoPresenciaDesocupado ||
                                                                                                                                 x.Causa == eCausaVideo.LazoIntermedioOcupado ||
                                                                                                                                 x.Causa == eCausaVideo.LazoIntermedioDesocupado));

                                            oVehiculo.ListaInfoFoto.Add(oFoto);
                                        }
                                        else
                                            oVehiculo.ListaInfoFoto.Add(oFoto);
                                    }
                                    else
                                        sacarFoto = false;
                                }
                                else
                                {
                                    // Chequea si la lista ya contiene una foto de tipo manual
                                    bool containsItem = false;

                                    if (oVehiculo.ListaInfoFoto.Count > 0)
                                        containsItem = oVehiculo.ListaInfoFoto.Any(item => item != null && item.EsManual == true);

                                    // Si contiene una foto manual, la reemplaza. Si no, agrega la foto manual a la lista
                                    if (containsItem)
                                        oVehiculo.ListaInfoFoto[oVehiculo.ListaInfoFoto.FindIndex(ind => ind != null && ind.EsManual == true)] = oFoto;
                                    else
                                        oVehiculo.ListaInfoFoto?.Add(oFoto);
                                }

                                if (sacarFoto || esManual || sensor == eCausaVideo.Manual)
                                {
                                    _logger.Trace("LogicaViaDinamica::CapturaFoto -> Saco foto FRONTAL");
                                    ModuloFoto.Instance.SacarFoto(oVehiculo, sensor, esManual, oFoto);
                                }
                                else
                                {
                                    _logger.Info("LogicaViaDinamica::CapturaFoto -> NO saca foto FRONTAL, por foto con mismo nombre en la lista");
                                }
                            }
                        }
                        else
                        {
                            _logger.Trace("LogicaViaDinamica::CapturaFoto -> No se saca foto FRONTAL por config de base");
                        }

                        sacarFoto = false;

                        // CAMARA LATERAL
                        sacarFoto = oVideoAcc.ComienzaGrabacion == "S" &&
                                    (ModuloBaseDatos.Instance.ConfigVia.Video == "F" || ModuloBaseDatos.Instance.ConfigVia.Video == "S"); // Chequear si este es para foto siempre

                        // Las fotos manuales solo se sacan con la frontal
                        if (sacarFoto)
                        {
                            InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Lateral), eCamara.Lateral, eTipoMedio.Foto, sensor, false);
                            if (oFoto != null)
                            {
                                if (oVehiculo.ListaInfoFoto == null)
                                    oVehiculo.ListaInfoFoto = new List<InfoMedios>();

                                if (!oVehiculo.ListaInfoFoto.Any(x => x != null && x.NombreMedio == oFoto.NombreMedio))
                                {
                                    eCausaVideo causaAux = sensor;

                                    if (oVehiculo.ListaInfoFoto.Any(x => x != null && x.Camara == eCamara.Lateral && x.Causa == causaAux))
                                    {
                                        oVehiculo.ListaInfoFoto[oVehiculo.ListaInfoFoto.FindIndex(x => x != null && x.Camara == eCamara.Lateral && x.Causa == causaAux)] = oFoto;
                                    }
                                    // Si se debe tomar una foto por pagado, se descartan las fotos tomadas por lazos
                                    else if (causaAux == eCausaVideo.Pagado || causaAux == eCausaVideo.PagadoExento || causaAux == eCausaVideo.PagadoTelepeaje)
                                    {
                                        oVehiculo.ListaInfoFoto.RemoveAll(x => x != null && x.Camara == eCamara.Lateral && (x.Causa == eCausaVideo.LazoPresenciaOcupado ||
                                                                                                                             x.Causa == eCausaVideo.LazoPresenciaDesocupado ||
                                                                                                                             x.Causa == eCausaVideo.LazoIntermedioOcupado ||
                                                                                                                             x.Causa == eCausaVideo.LazoIntermedioDesocupado));

                                        oVehiculo.ListaInfoFoto.Add(oFoto);
                                    }
                                    else
                                        oVehiculo.ListaInfoFoto.Add(oFoto);
                                }
                                else
                                    sacarFoto = false;

                                if (sacarFoto)
                                {
                                    _logger.Trace("LogicaViaDinamica::CapturaFoto -> Saco foto con LATERAL");
                                    ModuloFoto.Instance.SacarFoto(oVehiculo, sensor, esManual, oFoto);
                                }
                                else
                                {
                                    _logger.Info("LogicaViaDinamica::CapturaFoto -> NO saca foto LATERAL, por foto con mismo nombre en la lista");
                                }
                            }
                        }
                        else
                        {
                            _logger.Trace("LogicaViaDinamica::CapturaFoto -> No se saca LATERAL foto por config de base");
                        }

                    }
                    else
                        _logger.Trace("No se encontro la accion en la base de datos ");
                }
                else
                    _logger.Info("CapturaFoto -> Vehiculo en null");
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
        private string ObtenerNombreFoto(eCamara camara)
        {
            int numeroVia;
            ConfigVia configVia = ModuloBaseDatos.Instance.ConfigVia;
            if (camara == eCamara.Escape)
                numeroVia = configVia.NumeroViaEscape;
            else
                numeroVia = configVia.NumeroDeVia;
            return $"{configVia.NumeroDeEstacion.ToString().PadLeft(2, '0')}{numeroVia.ToString().PadLeft(3, '0')}{DateTime.Now.ToString("yyyyMMddHHmmssffffff")}" + camara.GetDescription() + ".jpg";
        }

        /// <summary>
        /// Genera nombre de archivo de video
        /// </summary>
        /// <returns></returns>
        private string ObtenerNombreVideo(eCamara camara)
        {
            int numeroVia;
            ConfigVia configVia = ModuloBaseDatos.Instance.ConfigVia;
            if (camara == eCamara.Escape)
                numeroVia = configVia.NumeroViaEscape;
            else
                numeroVia = configVia.NumeroDeVia;
            return $"{configVia.NumeroDeEstacion.ToString().PadLeft(2, '0')}{numeroVia.ToString().PadLeft(3, '0')}{DateTime.Now.ToString("yyyyMMddHHmmssffffff")}" + camara.GetDescription() + ".mp4";
        }

        override public void LazoPresenciaEgreso(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();
            char respuestaDAC = ' ';
            _logger.Info("******************* LazoPresenciaEgreso -> Ingreso[{Name}]", RespDAC);
            try
            {
                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                _bEnLazoPresencia = false;

                // No se hace ni estuvo abierta en sentido opuesto
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(false))
                {
                    _logger.Info("Desactivo Antena");
                    DesactivarAntenaTimer(DAC_PlacaIO.Instance.EstaOcupadoSeparadorEntrada()); //Arranca un timer diferenciado por modelo de vía

                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    DecideCaptura(eCausaVideo.LazoPresenciaDesocupado, _ultVehiculoAntena);

                    ActualizarEstadoSensores();
                    UpdateOnline();
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
        override public void InicioColaOn(bool esModoForzado, short RespDAC)
        {
            LoguearColaVehiculos();
            bool bSendLogica = false;
            InfoTagLeido oInfoTagLeido = null;
            char respuestaDAC = ' ';

            _logger.Info("******************* InicioColaOn -> Ingreso [{Name}]", RespDAC);
            try
            {
                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                if (respuestaDAC != '1')
                {
                    //Si P1 esta libre genero un evento de falla crítica y log se sensores
                    if (!GetVehiculo(eVehiculo.eVehP1).Ocupado)
                    {
                        //Solo es falla si era hacia adelante
                        if (respuestaDAC == 'D')
                        {
                            _logger.Info("OnInicioColaOn -> Vehículo eVehP1 libre ERROR!!!!!!!");

                            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    bSendLogica = true;
                        }

                        //Marco P1 como ocupado y con error de logica, le asigno número de vehículo
                        GetVehiculo(eVehiculo.eVehP1).Ocupado = true;
                        GetVehiculo(eVehiculo.eVehP1).ErrorDeLogica = true;
                        AsignarNumeroVehiculo(eVehiculo.eVehP1);

                        //Nos comimos la activacion de la antena, lo hacemos ahora
                        _logger.Info("OnInicioColaON->Nos comimos la activacion de la antena, lo hacemos ahora");

                        ActivarAntena('F', eCausaLecturaTag.eCausaLazoPresencia);

                    }

                    if (respuestaDAC == 'D')
                    {
                        //Si no estamos en quiebre ni en modo autista
                        if (_logicaCobro.ModoQuiebre == eQuiebre.Nada && !_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                        {
                            //Si P1 no tiene tag logueamos
                            // y generamos el evento
                            if (!GetVehiculo(eVehiculo.eVehP1).EstaPagado)
                            {
                                _logger.Info("OnInicioColaOn -> Vehiculo sin Pase (eVehP1)");
                            }
                        }

                        //Inicio el timer para obtener la velocidad de eVehP1 	
                        _timerObtenerVelocidad.Start();
                    }
                }

                //Recorro la lista de tags leidos y saco el vehículo más viejo
                int pos = 0;
                try
                {
                    while (pos < _lstInfoTagLeido.Count)
                    {
                        oInfoTagLeido = new InfoTagLeido();

                        oInfoTagLeido = _lstInfoTagLeido[pos];

                        //Si me avisan Retroceso para lectura
                        //asigno al de adelante, sino al de atras
                        if (respuestaDAC == '1')
                            oInfoTagLeido.BorrarVehiculoMayor();
                        else
                            oInfoTagLeido.BorrarVehiculoMenor(0);

                        //Si la cantidad de vehiculos es 1 y hay un tag leído llamo a AsignarTagAVehiculo
                        if (oInfoTagLeido.GetCantVehiculos() == 1 && oInfoTagLeido.GetInfoTag().Init)
                        {
                            if (AsignarTagAVehiculo(oInfoTagLeido.GetInfoTag(), oInfoTagLeido.GetNumeroVehiculo(0)))
                            {
                                //Comienzo nuevamente desde el principio porque AsignarTagAVehiculo
                                //sacó el elemento de la lista
                                pos = 0;

                                if (_lstInfoTagLeido.Count == 0)
                                    break;
                            }
                            else
                            {
                                //Avanzo al siguiente elemento
                                pos++;
                            }
                        }
                        else
                        {
                            //Avanzo al siguiente elemento
                            pos++;
                        }
                    }
                }
                catch
                {
                    _logger.Info("OnInicioColaON -> Error al quitar vehiculo [{0}]", pos);
                }

                //Tambien saco vehiculos del tag que estamos leyendo
                if (respuestaDAC == '1')
                    _infoTagLeido.BorrarVehiculoMayor();
                else
                    _infoTagLeido.BorrarVehiculoMenor(0);

                if (respuestaDAC != '1')
                {
                    //Indico si ingreso en sentido contrario
                    //Si recibo 'T' ingreso hacia atras
                    GetVehiculo(eVehiculo.eVehP1).IngresoSentidoContrario = (respuestaDAC == 'T');

                    //Adelanto todos los vehículos
                    AdelantarVehiculosModoD();

                    //Busco el vehículo más adelantado no vacío de atrás hacia adelante (eVehC0 a eVehP3)
                    //y si tiene tag habilitado levanto la barrera
                    int i = (int)eVehiculo.eVehC0;
                    bool bEnc = false;
                    while (i >= (int)eVehiculo.eVehP3 && !bEnc)
                    {
                        bEnc = GetVehiculo((eVehiculo)i).NoVacio;
                        i--;
                    }

                    //Me fijo si encontré un vehículo
                    if (bEnc)
                    {
                        //Encontré un vehículo (i+1), si tiene tag pagado abro la barrera
                        i++;
                        _logger.Info("OnInicioColaOn -> Hay 1 vehículo ocupado. eVehiculo:[{Name}]", ((eVehiculo)i).ToString());

                        if (GetVehiculo((eVehiculo)i).EstaPagado && (_logicaCobro.Estado != eEstadoVia.EVAbiertaVenta
                            && _logicaCobro.Estado != eEstadoVia.EVCerrada))
                        {
                            _logger.Info("OnInicioColaOn -> Subir barrera, tag habilitado. eVehiculo:[{Name}]", ((eVehiculo)i).ToString());

                            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

                            //Si no tengo ninguna foto, inicio la captura
                            if (!GetVehiculo((eVehiculo)i).ListaInfoFoto.Any(x => x != null))
                            {
                                InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, eCausaVideo.PagadoTelepeaje);
                                ModuloFoto.Instance.SacarFoto(new Vehiculo(), eCausaVideo.PagadoTelepeaje, false, oFoto);
                                GetVehiculo((eVehiculo)i).ListaInfoFoto.Add(oFoto);
                            }
                            //Si no tengo ningun video, inicio la captura
                            if (!GetVehiculo((eVehiculo)i).ListaInfoVideo.Any(x => x != null))
                            {
                                eCausaVideo causa = eCausaVideo.PagadoTelepeaje;
                                Vehiculo veh = GetVehiculo((eVehiculo)i);
                                CapturaVideo(ref veh, ref causa);
                            }

                            // Actualiza el estado de los mimicos en pantalla
                            Mimicos mimicos = new Mimicos();
                            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                        }
                    }

                    SetVehiculoIng();

                    //Si no hubo falla de sensores mando las fallas de lógica que
                    //se produjeron (se envian dentro del método GrabarLogSensores)
                    //Si no está la vía en sentido opuesto
                    if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                    {
                        if (!FallaSensoresDAC())
                        {
                            if (bSendLogica)
                            {
                                GrabarLogSensores("OnInicioColaON", eLogSensores.Logica_Sensores);
                            }
                        }
                    }
                }

                RegularizarFilaVehiculos();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* InicioColaOn -> Salida");
        }

        /// <summary>
        /// Deja la fila de vehiculos regularizada a partir de P1
        /// </summary>
        public override void RegularizarFilaVehiculos()
        {
            _logger.Info("RegularizarFilaVehiculos -> Inicio");
            LoguearColaVehiculos();

            int nOcupados = 0, noVacios = 0;
            int nSkip = (int)eVehiculo.eVehP3; //Se salta los primeros 4 elementos (OnLine, Ing, Tra, Ant)
            int nTake = (int)eVehiculo.eVehC0 - (int)eVehiculo.eVehAnt; //Se toman solo 5 elementos (C0, C1, P1, P2, P3)
            bool b1 = false, b2 = false, b3 = false;

            //Bloquear modificacion de vehiculos
            lock (_lockAsignarTag)
            {
                //Recorrer la fila de vehiculos contando cuantos Ocupados y cuantos No Vacios hay (de C0 a P3)
                try
                {
                    nOcupados = _vVehiculo.Skip(nSkip).Take(nTake).Where(x => x.Ocupado).ToList().Count();
                    noVacios = _vVehiculo.Skip(nSkip).Take(nTake).Where(x => x.NoVacio).ToList().Count();
                }
                catch (Exception e)
                {
                    _loggerExcepciones?.Error(e);
                }

                if (nOcupados == 0 && (noVacios > 0 && noVacios <= 3))
                {
                    //Buscar el primer vehiculo No Vacio (de C0 a P3) y moverlo a VehiculoPrimero
                    for (int i = (int)eVehiculo.eVehC0; i >= (int)eVehiculo.eVehP3; i--)
                    {
                        if (GetVehiculo((eVehiculo)i).NoVacio)
                        {
                            if (!b1)
                            {
                                _VehPrimero.CopiarVehiculo(ref _vVehiculo[i]);
                                b1 = true;
                            }
                            else if (!b2)
                            {
                                _VehSegundo.CopiarVehiculo(ref _vVehiculo[i]);
                                b2 = true;
                            }
                            else if (!b3)
                            {
                                _VehTercero.CopiarVehiculo(ref _vVehiculo[i]);
                                b3 = true;
                            }
                            _vVehiculo[i] = new Vehiculo();
                        }
                    }

                    //P1 <- VehiculoPrimero
                    if (b1)
                        _vVehiculo[(int)eVehiculo.eVehP1].CopiarVehiculo(ref _VehPrimero);
                    //P2 <- VehiculoSegundo
                    if (b2)
                        _vVehiculo[(int)eVehiculo.eVehP2].CopiarVehiculo(ref _VehSegundo);
                    //P3 <- VehiculoTercero
                    if (b3)
                        _vVehiculo[(int)eVehiculo.eVehP3].CopiarVehiculo(ref _VehTercero);
                    _VehPrimero = new Vehiculo();
                    _VehSegundo = new Vehiculo();
                    _VehTercero = new Vehiculo();
                }
            }

            _logger.Info("RegularizarFilaVehiculos -> Fin");
            LoguearColaVehiculos();
        }

        /// <summary>
        /// Muestra los datos en pantalla segun el estado en que se encuentre
        /// </summary>
        /// <param name="bActivacionAntena">flag para indicar si se llamó por activación de la antena</param>
        /// <param name="bForzarMuestra">se muestran los datos en el dpy y en el monitor del vehículo aunque no haya cambiado</param>
        /// <param name="bNoMuestraLibre">Si es true no se muestra en el monitor Via Libre</param>
        /// <param name="bEstamosCobrando">true cuando recien cobramos para mostrarlo</param>
        private void SetVehiculoIng(bool bActivacionAntena = false, bool bForzarMuestra = false, bool bNoMuestraLibre = false, bool bEstamosCobrando = false)
        {
            //bool		bCategoByRunner;
            bool bMostrar = false;
            eVehiculo vehiculoIng = 0;
            bool bMostrarVehiculo = true;

            if (_logicaCobro.Estado == eEstadoVia.EVAbiertaVenta)
            {
                _logger.Info("SetVehiculoIng -> Estado igual a Ventas");
                return;
            }
            _logger.Info("SetVehiculoIng -> INICIO Anterior [{Name}] [{Name}] ActivacionAntena?:[{Name}] Forzar?:[{Name}] NoMuestraLibre?:[{Name}] EstamosCobrando? [{Name}]", ((eVehiculo)_eVehiculoIngAnt).ToString(), _ulVehiculoIngAnt, bActivacionAntena, bForzarMuestra, bNoMuestraLibre, bEstamosCobrando);
            //Seteamos el estado y mostramos el primer vehiculo, 
            //aun estando sobre el BPA pero sin pagado
            //Si está vacio que devuelva P1
            vehiculoIng = GetVehiculoIng();

            // Si la via no esta cerrada
            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
            {
                //Si el estado no es quiebre de barrera liberado 
                if (!(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado))
                {
                    //Si el vehículo asignado está vacio entonces el estado es EVAbiertaLibre
                    //y semáforo de paso en rojo
                    if (vehiculoIng == eVehiculo.eVehAnt ||
                        (!GetVehiculo(vehiculoIng).NoVacio && GetVehiculo(vehiculoIng).Categoria == 0
                        && !GetVehiculo(vehiculoIng).EstaPagado))
                    {
                        _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;
                        if (!_statusBarreraQ)
                        {
                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                        }
                        //Limpiamos los datos (porque el VehAnt ahora no está libre                        
                        bMostrarVehiculo = false;
                    }
                    else
                    {
                        //Si el vehículo está pagado cambio el estado
                        //JAS 7/9/2009 Si le estamos cobrando devolvemos estado Pagado para no permitir otro cobro
                        //a EVAbiertaPag, sino a EVAbiertaCat y semáforo de paso en verde
                        if (GetVehiculo(vehiculoIng).EstaPagado || GetVehiculo(vehiculoIng).CobroEnCurso)
                        {
                            _logicaCobro.Estado = eEstadoVia.EVAbiertaPag;

                            //Si el vehiculo ya llego a la salida
                            //ya ponemos el semaforo en rojo
                            //SI recien cobramos lo dejamos provisoriamente en verde
                            if (GetVehiculo(vehiculoIng).SalidaON && !bEstamosCobrando)
                            {
                                //Mostramos los datos del que viene detras
                                vehiculoIng = GetVehiculoIng(true);
                                _logger.Debug("SetVehiculoIng -> Saco Vehic:[{Name}]", vehiculoIng.ToString());
                                if ((!GetVehiculo(vehiculoIng).NoVacio && GetVehiculo(vehiculoIng).Categoria == 0))
                                {
                                    if (!_statusBarreraQ)
                                    {
                                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                    }
                                    //Limpiamos los datos (porque el VehAnt ahora no está libre
                                    bMostrarVehiculo = false;
                                }
                                else if (GetVehiculo(vehiculoIng).EstaPagado)
                                {
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                }
                                else
                                {
                                    if (!_statusBarreraQ)
                                    {
                                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                    }

                                    if (!GetVehiculo(vehiculoIng).CobroEnCurso)
                                    {
                                        //Verificamos tabulacion y recorrido
                                        //Si tenia un tag leido le forzamos la tabulacion
                                        if (GetVehIngCat().InfoTag.NumeroTag != ""
                                            && GetVehIngCat().InfoTag.TagOK)
                                            TagIpicoVerificarManual();
                                    }
                                }
                            }
                            else if (GetVehiculo(vehiculoIng).EstaPagado)
                            {
                                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                            }
                            else
                            {
                                if (!_statusBarreraQ)
                                {
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                                }
                                if (GetVehiculo(vehiculoIng).CobroEnCurso)
                                    bMostrarVehiculo = false;
                            }
                        }
                        else
                        {
                            if (GetVehiculo(vehiculoIng).Categoria == 0)
                            {
                                _logicaCobro.Estado = eEstadoVia.EVAbiertaLibre;
                            }
                            else
                            {
                                _logicaCobro.Estado = eEstadoVia.EVAbiertaCat;
                            }

                            if (!_statusBarreraQ)
                            {
                                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                            }

                            //Verificamos tabulacion y recorrido
                            //Si tenia un tag leido le forzamos la tabulacion
                            if (GetVehIngCat().InfoTag.NumeroTag != ""
                                && GetVehIngCat().InfoTag.TagOK)
                                TagIpicoVerificarManual();

                            if (GetVehiculo(vehiculoIng).CobroEnCurso)
                                bMostrarVehiculo = false;
                        }
                    }
                    if (bMostrarVehiculo)
                    {
                        //Si cambió el vehículo Ing muestro los datos o si se fuerza
                        bMostrar = false;
                        if (vehiculoIng != _eVehiculoIngAnt || _ulVehiculoIngAnt != GetVehiculo(vehiculoIng).NumeroVehiculo || bForzarMuestra || _bUltEstadoActivAntena)
                        {
                            bMostrar = true;
                            MostrarDatosVehiculo(GetVehiculo(vehiculoIng).NumeroVehiculo, bNoMuestraLibre, true, vehiculoIng);
                        }
                        else
                        {
                            bMostrar = true;
                            MostrarDatosVehiculo(GetVehiculo(vehiculoIng).NumeroVehiculo, bNoMuestraLibre, false, vehiculoIng);
                        }
                    }
                }
            }

            _logger.Info("SetVehiculoIng Vehic:[{Name}] Estado:[{Name}] NumeroVehiculo: [{Name}]", vehiculoIng, _logicaCobro.Estado, GetVehiculo(vehiculoIng).NumeroVehiculo);

            _eVehiculoIngAnt = vehiculoIng;
            _ulVehiculoIngAnt = GetVehiculo(vehiculoIng).NumeroVehiculo;

            //Reseteo el flag de ultimo estado y si se invocó el método con bActivaciónAntena
            //lo seteo
            _bUltEstadoActivAntena = false;
            if (bActivacionAntena)
                _bUltEstadoActivAntena = true;

            //Si está Pagado asigno los datos de cobro del primer vehiculo al online
            if (_logicaCobro.Estado == eEstadoVia.EVAbiertaPag)
            {
                GetVehOnline().SetDatosPago(GetVehiculo(GetVehiculoPrimero()));
            }

            if (bMostrar)
                UpdateOnline();
            _logger.Debug("SetVehiculoIng -> Fin");
        }

        private void MostrarDatosVehiculo(ulong ulNumVehiculo, bool bNoMuestraLibre, bool v, eVehiculo eVeh = eVehiculo.eVehP1)
        {
            _logger.Debug("MostrarDatosVehiculo -> Inicio. bNoMuestraLibre[{Name}]. Vehiculo[{Name}]", bNoMuestraLibre, ulNumVehiculo);
            // Actualiza estado de vehiculo en pantalla
            if (!bNoMuestraLibre)
            {
                string sMarca = "", sColor = "", sModelo = "";
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClienteDB oCliente = new ClienteDB();
                Vehiculo veh;
                if (ulNumVehiculo > 0)
                    veh = GetVehiculo(BuscarVehiculo(ulNumVehiculo));
                else
                    veh = GetVehiculo(eVeh);



                if (veh.InfoTag.TipoTag != eTipoCuentaTag.Ufre ||
                            (veh.InfoTag.TipoTag == eTipoCuentaTag.Ufre && !veh.EstaPagado && veh.EsperaRecargaVia))
                {
                    oCliente.Nombre = veh.InfoTag.NombreCuenta?.Trim();
                    oCliente.NumeroDocumento = veh.InfoTag.Ruc?.Trim();
                }

                ClassUtiles.InsertarDatoVia(veh, ref listaDatosVia);

                // Para que sea solo con tags
                if (!string.IsNullOrEmpty(veh.InfoTag?.NumeroTag))
                {
                    sMarca = veh.InfoTag.Marca?.ToUpper() == "INDEFINIDO" ? "" : veh.InfoTag.Marca?.ToUpper();
                    sModelo = veh.InfoTag.Modelo?.ToUpper() == "INDEFINIDO" ? "" : veh.InfoTag.Modelo?.ToUpper();
                    sColor = veh.InfoTag.Color?.ToUpper() == "INDEFINIDO" ? "" : veh.InfoTag.Color?.ToUpper();

                    if (veh.InfoTag.GetTagHabilitado() || veh.ListaRecarga.Any(x => x != null))
                    {
                        if (veh.InfoTag.TipoTag != eTipoCuentaTag.Ufre ||
                            (veh.InfoTag.TipoTag == eTipoCuentaTag.Ufre && !veh.EstaPagado && veh.EsperaRecargaVia))
                        {
                            // Actualiza el mensaje en pantalla y display
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, veh.InfoTag?.Mensaje, false);
                            ModuloDisplay.Instance.Enviar(eDisplay.VAR, veh, veh.InfoTag?.MensajeDisplay);
                        }

                        if (veh.InfoTag.TipoTag == eTipoCuentaTag.Ufre && !veh.EstaPagado && veh.EsperaRecargaVia)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.TeclaViaje, false);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, sModelo + " " + sMarca + " " + sColor, false);
                        }
                        else if (veh.InfoTag.TipOp == 'C' && !veh.EstaPagado)
                        {
                            Causa causa = new Causa(eCausas.PagoTarjetaChip, ClassUtiles.GetEnumDescr(eCausas.PagoTarjetaChip));
                            List<DatoVia> listaDV = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(causa, ref listaDV);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDV);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, sModelo + " " + sMarca + " " + sColor, false);
                        }
                        else if (veh.InfoTag.TipoTag == eTipoCuentaTag.Prepago)   //Muestro el saldo del prepago
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Utiles.Utiles.Traduccion.Traducir("Saldo") + ": " + ClassUtiles.FormatearMonedaAString(veh.InfoTag.SaldoFinal) + " - "
                                + sModelo + " " + sMarca + " " + sColor, false);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, string.Empty, false);
                        }
                        else
                        {
                            if (veh.InfoTag.TipoTag != eTipoCuentaTag.Ufre)
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, sModelo + " " + sMarca + " " + sColor, false);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, string.Empty, false);
                            }
                        }
                        ClassUtiles.InsertarDatoVia(oCliente, ref listaDatosVia);
                    }
                    else
                    {
                        if (veh.InfoTag?.ErrorTag == eErrorTag.Desconocido)
                        {
                            if (!veh.TieneTag)
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Utiles.Utiles.Traduccion.Traducir("TAG DESCONOCIDO"), false);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, veh.InfoTag?.NumeroTag.Truncate(24), false);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, string.Empty, false);
                                ModuloDisplay.Instance.Enviar(eDisplay.VAR, veh, "DESCONOCIDO");
                            }
                        }
                        else if (veh.InfoTag?.ErrorTag == eErrorTag.SinSaldo &&
                                veh.InfoTag.TipoTag == eTipoCuentaTag.Ufre && !veh.EstaPagado)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, veh.InfoTag?.Mensaje + " - " +
                                Utiles.Utiles.Traduccion.Traducir("Saldo") + ": " + ClassUtiles.FormatearMonedaAString(veh.InfoTag.SaldoInicial), false);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.TeclaViaje, false);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, sModelo + " " + sMarca + " " + sColor, false);
                            ModuloDisplay.Instance.Enviar(eDisplay.VAR, veh, veh.InfoTag?.MensajeDisplay);
                        }
                        else
                        {
                            string sMensajeP, sMensajeD;
                            bool TagPagoEnViaPrepago = false;

                            if (veh.InfoTag?.ErrorTag == eErrorTag.NoHabilitado)
                            {
                                sMensajeP = string.IsNullOrEmpty(veh.InfoTag?.Mensaje) ? veh.InfoTag?.ErrorTag.ToString() : veh.InfoTag?.Mensaje;
                                //sMensajeP += " ( " + ClassUtiles.FormatearMonedaAString(veh.InfoTag.SaldoFinal ) + " )";

                                sMensajeD = string.IsNullOrEmpty(veh.InfoTag?.MensajeDisplay) ? veh.InfoTag?.ErrorTag.ToString() : veh.InfoTag?.MensajeDisplay;
                            }
                            else if (veh.InfoTag?.ErrorTag == eErrorTag.SinSaldo && veh.InfoTag.PagoEnViaPrepago)
                            {
                                sMensajeP = veh.InfoTag?.Mensaje;
                                sMensajeP += " - " + Utiles.Utiles.Traduccion.Traducir("Saldo") + " ( " + ClassUtiles.FormatearMonedaAString(veh.InfoTag.SaldoFinal) + " )";

                                sMensajeD = string.IsNullOrEmpty(veh.InfoTag?.MensajeDisplayAuxiliar) ? veh.InfoTag?.ErrorTag.ToString() : veh.InfoTag?.MensajeDisplayAuxiliar;
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.TeclaViajeRecarga, false);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, sModelo + " " + sMarca + " " + sColor, false);
                                TagPagoEnViaPrepago = true;
                            }
                            else
                            {
                                sMensajeP = string.IsNullOrEmpty(veh.InfoTag?.MensajeAuxiliar) ? veh.InfoTag?.ErrorTag.ToString() : veh.InfoTag?.MensajeAuxiliar;
                                sMensajeP += " ( " + ClassUtiles.FormatearMonedaAString(veh.InfoTag.SaldoFinal) + " )";

                                sMensajeD = string.IsNullOrEmpty(veh.InfoTag?.MensajeDisplayAuxiliar) ? veh.InfoTag?.ErrorTag.ToString() : veh.InfoTag?.MensajeDisplayAuxiliar;
                            }

                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, sMensajeP, false);
                            if (!TagPagoEnViaPrepago)
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Utiles.Utiles.Traduccion.Traducir("Patente") + $": {veh.InfoTag.Patente}" + " - "
                                    + sModelo + " " + sMarca + " " + sColor, false);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Nro: {veh.InfoTag.NumeroTag.Truncate(24)}", false);
                            }
                            ModuloDisplay.Instance.Enviar(eDisplay.VAR, veh, sMensajeD);

                            ClassUtiles.InsertarDatoVia(oCliente, ref listaDatosVia);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(oCliente.Nombre)) //para mostrar el RUC y Razon social
                    ClassUtiles.InsertarDatoVia(oCliente, ref listaDatosVia);

                if (veh.NoVacio) //si es un vehiculo vacio no enviamos nada a Pantalla porque pisa datos del veh
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
            }
            _logger.Debug("MostrarDatosVehiculo -> Fin");
        }

        /// <summary>
        /// Busca en la lista de vehículos el vehículo que tenga como número
        /// el valor ulNroVehiculo y asigna el tag oInfoTag si es que corresponde
        /// </summary>
        /// <param name="oInfoTag">datos leídos del tag</param>
        /// <param name="ulNroVehiculo">número de vehículo al que hay que asignar el tag</param>
        /// <returns></returns>
        private bool AsignarTagAVehiculo(InfoTag oInfoTag, ulong ulNroVehiculo)
        {
            bool bEnc = false, bRet = false;
            int i = 0;
            DateTime dtFechVal = DateTime.MinValue;

            bAsignandoTagAVehiculo = true;
            eVehiculo vehiculo = eVehiculo.eVehP1;
            Vehiculo oVeh = null, oVehAux = null, VehIng = null;
            _logger.Info("AsignarTagAVehiculo -> Inicio Tag:[{Name}] Vehiculo:[{Name}] TipOp:[{Name}]", oInfoTag.NumeroTag, ulNroVehiculo, oInfoTag.TipOp);
            VehIng = GetVehIng();

            if (VehIng.ListaRecarga.Any(x => x != null))
            {
                VehIng.InfoTag.ErrorTag = eErrorTag.SinSaldo;

                if (VehIng.NumeroVehiculo == 0)
                    vehiculo = GetVehiculoIng();
            }

            if (ulNroVehiculo > 0)
            {
                i = (int)eVehiculo.eVehP3;
                int total = (int)eVehiculo.eVehC0;

                //Busco en la lista de vehículos el vehículo con número ulNroVehiculo
                while (!bEnc && i <= total)
                {
                    oVeh = GetVehiculo((eVehiculo)i);

                    if (oVeh.NumeroVehiculo == ulNroVehiculo)
                        if (!oVeh.EstaPagado && !oVeh.InfoTag.GetTagHabilitado() && !oVeh.NoPermiteTag)
                            //Solo lo asigno si no está pagado
                            bEnc = true;
                    i++;
                }
            }
            else if (VehIng.ListaRecarga.Any(x => x != null)) // Si tiene una recarga
            {
                oVeh = GetVehiculo(vehiculo);
                bEnc = true;
            }

            //Si no lo encontré es porque ese vehiculo fue quitado de la vía
            //Si la lectura es muy vieja lo saco de la lista
            //Si no me fijo si hay un vehiculo posterior y se lo pongo a este
            if (!bEnc)
            {
                TimeSpan ctsDiff = DateTime.Now - oInfoTag.FechaPago;

                //si la antena dejó de leerlo generamos el evento, de lo contrario se lo asignamos a otro veh
                if (ctsDiff.TotalMilliseconds >= _timeoutTagsinVeh && ModuloAntena.Instance.AntenaNoRecibeMasLecturas(oInfoTag.NumeroTag))
                {
                    _logger.Info("AsignarTagAVehiculo -> No lo encontre, es viejo generamos MA [{Name}]", oInfoTag.FechaPago);

                    //La lectura es vieja, la descarto
                    //Quito el elemento de la lista que tiene como número de tag el de oInfoTag
                    //Generando un transito cerrado
                    bRet = LimpiarTagLeido(oInfoTag);

                    oVehAux = oVeh;
                    oVehAux.InfoTag = oInfoTag;

                    // Evento de tag sin vehiculo
                    if (oInfoTag.LecturaManual == 'N')
                        ModuloEventos.Instance.SetVehiculoSinTag(_logicaCobro.GetTurno, oVehAux, 0, '0');
                }
                else
                {
                    //Tratamos de asignarla a un nuevo vehiculo
                    //Buscamos el más adelantado libre
                    _logger.Info("AsignarTagAVehiculo -> No lo encontre, buscamos otro");
                    i = (int)eVehiculo.eVehP3;

                    while (!bEnc && i <= (int)eVehiculo.eVehC0)
                    {
                        oVeh = GetVehiculo((eVehiculo)i);
                        if (oVeh.EstaImpago && oVeh.NumeroVehiculo > ulNroVehiculo && !oVeh.NoPermiteTag)
                        {
                            _logger.Info("AsignarTagAVehiculo -> Encontramos vehiculo [{Name}]", ((eVehiculo)i).ToString());
                            //Si se encuentra otro veh, se guarda el nro de veh y el enum para enviar a asignarTag
                            vehiculo = (eVehiculo)i;
                            ulNroVehiculo = oVeh.NumeroVehiculo;
                            bEnc = true;
                        }
                        i++;
                    }

                }
            }

            if (bEnc)
            {
                char multiplesTags = !string.IsNullOrEmpty(oVeh.InfoTag.NumeroTag) && (oVeh.InfoTag.NumeroTag != oInfoTag.NumeroTag) ? '1' : '0';
                bool bReemplazar = true;

                _logger.Info("AsignarTagAVehiculo -> Encontramos el vehiculo, tiene multiples Tags? [{0}], LecturaManual? [{1}], " +
                             "Es CHIP? [{2}]", multiplesTags == '1' ? "SI" : "NO",
                             oInfoTag.LecturaManual == 'S' ? "SI" : "NO",
                             oInfoTag.TipOp == 'C' ? "SI" : "NO");

                if (multiplesTags == '1' && oInfoTag.LecturaManual != 'S' && oInfoTag.TipOp != 'C')
                {
                    _logger.Info("AsignarTagAVehiculo -> Tag del Vehiculo: TagOK? [{0}] ErrorTag [{1}]", oVeh.InfoTag.TagOK, oVeh.InfoTag.ErrorTag.ToString());
                    //verificar si hay que reemplazar el tag o no
                    if (oVeh.InfoTag.TagOK)
                        bReemplazar = false;
                    else if (oInfoTag.TagOK)
                        bReemplazar = true;
                    else if (oVeh.InfoTag.ErrorTag != eErrorTag.Desconocido)
                        bReemplazar = false;
                    else if (oInfoTag.ErrorTag == eErrorTag.Desconocido)
                        bReemplazar = false;
                }

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
                    bRet = DepartTagLeido(oInfoTag.NumeroTag);

                    _logger.Info("AsignarTagAVehiculo -> Tag:[{Name}] Vehículo:[{Name}] TipOp[{Name}]", oInfoTag.NumeroTag, ulNroVehiculo, oInfoTag.TipOp);

                    //revisar si el vehiculo sigue con el nro
                    if (oVeh.InfoTag.NumeroTag != "")
                    {
                        vehiculo = BuscarVehiculo(0, false, true, oVeh.InfoTag.NumeroTag);

                        if (GetVehiculo(vehiculo).NumeroVehiculo != ulNroVehiculo)
                            ulNroVehiculo = 0;
                    }

                    //revisamos que no este en una violacion para asignar el tag
                    if (!GetVehiculo(vehiculo).ProcesandoViolacion)
                    {
                        //Eliminamos el tag de la lista de la antena para poder leerlo nuevamente
                        if (!string.IsNullOrEmpty(GetVehiculo(vehiculo).InfoTag.NumeroTag))
                        {
                            ModuloAntena.Instance.BorrarTag(GetVehiculo(vehiculo).InfoTag.NumeroTag);
                            SetMismoTagIpico(GetVehiculo(vehiculo).InfoTag.NumeroTag);
                        }

                        //obtiene el vehiculo al que se asignó
                        vehiculo = AsignarTag(oInfoTag, ulNroVehiculo, vehiculo);

                        if (oInfoTag.ErrorTag != eErrorTag.Desconocido && oInfoTag.ErrorTag != eErrorTag.Error)
                        {
                            if (oInfoTag.TipoTag == eTipoCuentaTag.Exento)
                            {
                                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoExento, null, null, GetVehiculo(vehiculo));
                                ModuloVideoContinuo.Instance.ActualizarTurno(_logicaCobro.GetTurno);
                            }
                            else if (oInfoTag.TipoTag != eTipoCuentaTag.Ufre && oInfoTag.TipoTag != eTipoCuentaTag.Nada)
                            {
                                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoTagChip, null, null, GetVehiculo(vehiculo), oInfoTag);
                                ModuloVideoContinuo.Instance.ActualizarTurno(_logicaCobro.GetTurno);
                            }
                        }
                        else if (oInfoTag.ErrorTag == eErrorTag.Desconocido)
                        {
                            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoTagChip, null, null, null, oInfoTag);
                            ModuloVideoContinuo.Instance.ActualizarTurno(_logicaCobro.GetTurno);
                        }

                        //si está habilitado sacar una foto
                        if (oInfoTag.GetTagHabilitado())
                        {
                            //Elimino del vehiculo las fotos de la lectura del tag
                            GetVehiculo(vehiculo).ListaInfoFoto.RemoveAll(x => x != null && x.Causa == eCausaVideo.TagLeidoPorAntena);
                            //Saco una foto del momento en el que llega el tag por antena y la agrego al oInfoTag
                            InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, eCausaVideo.PagadoTelepeaje);
                            ModuloFoto.Instance.SacarFoto(GetVehiculo(vehiculo), eCausaVideo.PagadoTelepeaje, false, oFoto);
                            if (GetVehiculo(vehiculo).ListaInfoFoto.Any(x => x != null && x.Camara == eCamara.Frontal && x.Causa == eCausaVideo.PagadoTelepeaje))
                                GetVehiculo(vehiculo).ListaInfoFoto[GetVehiculo(vehiculo).ListaInfoFoto.FindIndex(x => x != null && x.Camara == eCamara.Frontal && x.Causa == eCausaVideo.PagadoTelepeaje)] = oFoto;
                            else
                                GetVehiculo(vehiculo).ListaInfoFoto.Add(oFoto);
                        }

                        //Si el vehículo 
                        // es P1 y C1 y C0 están vacíos
                        //o es C1 y C0 está vacío
                        //o es C0		

                        bool bForzarMuestra = false;

                        if (
                            (vehiculo == eVehiculo.eVehP1 && !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                            (vehiculo == eVehiculo.eVehC1 && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                            (vehiculo == eVehiculo.eVehC0))
                        {
                            _logger.Info("AsignarTagAVehiculo -> Cumple condición T_Vehiculo:[{Name}]", ((eVehiculo)vehiculo).ToString());

                            //Si es un tag habilitado levanto la barrera
                            if (oInfoTag.GetTagHabilitado())
                            {
                                _logger.Info("AsignarTagAVehiculo -> Tag habilitado, subo barrera? T_Vehiculo:[{Name}]", ((eVehiculo)vehiculo).ToString());

                                if (oInfoTag.TipoTag != eTipoCuentaTag.Ufre && oInfoTag.TipOp != 'C' && !GetVehiculo(vehiculo).CobroEnCurso)
                                {
                                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                                    _logger.Info("AsignarTagAVehiculo -> BARRERA ARRIBA!!");
                                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                }
                                Mimicos mimicos = new Mimicos();
                                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                            }
                            else
                            {
                                _logger.Info("AsignarTagAVehiculo -> Tag no habilitado T_Vehiculo:[{Name}]", ((eVehiculo)vehiculo).ToString());
                            }

                            bForzarMuestra = true;
                        }

                        //Acabamos de cobrar
                        //SI es lectura manual o chip lo mostramos aun en el BPA
                        if (oInfoTag.LecturaManual == 'S' || oInfoTag.TipOp == 'C')
                            SetVehiculoIng(false, false, false, true);
                        else
                            SetVehiculoIng(false, bForzarMuestra);
                    }
                    else
                    {
                        _logger.Info("AsignarTagAVehiculo -> Encolamos el Tag porque el veh está en medio de una violacion [{0}]", oInfoTag.NumeroTag);
                        QueueTagLeido(oInfoTag);
                    }
                }
                else
                {
                    _logger.Info("AsignarTagAVehiculo -> Encolamos el Tag [{0}]", oInfoTag.NumeroTag);
                    QueueTagLeido(oInfoTag);
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
                        bRet = DepartTagLeido(oInfoTag.NumeroTag);
                    }
                }
            }

            bAsignandoTagAVehiculo = false;
            _logger.Info("AsignarTagAVehiculo -> Fin");
            //MostrarColaVehiculos();
            //UpdatePagoAdelantado();
            return bRet;
        }

        private eVehiculo AsignarTag(InfoTag oTag, ulong ulNroVehiculo = 0, eVehiculo vehiculo = eVehiculo.eVehP1)
        {
            byte btTipoTarifa = 0;
            char cTipOp = ' ', cFormaDescuento = ' ', cLecturaManual = ' ';
            decimal ulTarifaOriginal = 0, ulIVA = 0;
            decimal ulTarifa = 0m;
            string sTipDH = "";
            bool TieneFormaPago = false;
            //InfoClearing oInfoAutPaso;
            DateTime dtFechaPago = DateTime.MinValue;
            string sPatente = "";
            Vehiculo vehLocal;

            lock (_lockAsignarTag)
            {
                vehLocal = new Vehiculo();

                if (ulNroVehiculo > 0)
                    vehiculo = BuscarVehiculo(ulNroVehiculo);

                Vehiculo vehAux = GetVehiculo(vehiculo);
                vehLocal.CopiarVehiculo(ref vehAux);


                //vehLocal = GetVehiculo(vehiculo);

                _logger.Info("AsignarTag -> Inicio. Vehículo:[{Name}] T_Vehiculo:[{Name}] Tag:[{Name}] Tipop[{Name}] ulNroVehiculo[{Name}]", vehLocal.NumeroVehiculo, vehiculo.ToString(), oTag.NumeroTag, oTag.TipOp, ulNroVehiculo);
                //Asigno el ultimo tag leido
                _ultTagLeido = oTag.NumeroTag;
                _ultTagLeidoTiempo = DateTime.Now;

                // Si el veh no tiene forma de pago asignada y no hay un cobro en curso para el mismo, asignamos el Tag
                if (vehLocal.FormaPago == eFormaPago.Nada && vehLocal.CobroEnCurso == false)
                {
                    if (ulNroVehiculo != vehLocal.NumeroVehiculo)
                    {
                        if (ulNroVehiculo > 0)
                            vehiculo = BuscarVehiculo(ulNroVehiculo);

                        vehAux = GetVehiculo(vehiculo);
                        vehLocal.CopiarVehiculo(ref vehAux);
                    }

                    //Asigno los datos del tag en la posición eVehIng del vector de tags
                    vehLocal.InfoTag = oTag;

                    if (vehLocal.ListaRecarga.Any(x => x != null))
                        vehLocal.InfoTag.ErrorTag = eErrorTag.NoError;

                    oTag.GetDatosVehiculo(ref cTipOp, ref btTipoTarifa,
                                            ref cFormaDescuento, ref cLecturaManual,
                                            ref ulTarifa, ref ulTarifaOriginal,
                                            /*oInfoAutPaso,*/ref dtFechaPago,
                                            ref sTipDH, ref sPatente, ref ulIVA);

                    vehLocal.TipOp = cTipOp;
                    //vehLocal.tInfoAutPasoRec(oInfoAutPaso);
                    vehLocal.Fecha = dtFechaPago;

                    vehLocal.NumeroBoleto = oTag.NumeroTag;
                    vehLocal.TipBo = oTag.TipBo;
                    vehLocal.Patente = sPatente;

                    vehLocal.Reversa = false;
                    vehLocal.CodigoObservacion = 0;
                    vehLocal.CategoByRunner = false;

                    if (oTag.ErrorTag != eErrorTag.Desconocido)
                        vehLocal.TieneTag = true;

                    //La categoria la asignamos si está habilitado
                    // o para ciertos errores, donde el cajero podria hacer algo para cobrar con este Tag
                    if (oTag.GetTagHabilitado()
                        || oTag.ErrorTag == eErrorTag.SinViajes
                        || oTag.ErrorTag == eErrorTag.SinSaldo
                        //|| oTag.GetErrorTag() == DEF_TAG_VENCIDO
                        //|| oTag.GetErrorTag() == DEF_TAG_NOHABIESTAC
                        //|| oTag.GetErrorTag() == DEF_TAG_VEHIVENCIDO
                        || oTag.ErrorTag == eErrorTag.AbonoVencido)
                    {
                        //Solo para tag
                        if (cTipOp == 'T' || cTipOp == 'O')
                        {
                            //Asigno la categoría del tag al vehículo
                            vehLocal.Categoria = oTag.Categoria;
                            vehLocal.CategoDescripcionLarga = oTag.CategoDescripcionLarga;
                        }

                        vehLocal.Tarifa = ulTarifa;  //Porque asigna tarifa?
                        vehLocal.TipoDiaHora = sTipDH;
                        vehLocal.InfoTag.TipDH = sTipDH;
                        vehLocal.IVA = ulIVA;
                        vehLocal.InfoPagado.CargarValores(vehLocal);
                    }
                    else
                    {
                        _logger.Info("AsignarTag -> No Habilitado");
                    }

                    //Solo asigno la tarifa si está habilitado 
                    //Se cobra tarifa manual si es cualquier otro 
                    if (oTag.GetTagHabilitado())
                    //|| oInfoAutPaso.GetTipoLectura() == 'T' )
                    {
                        vehLocal.Tarifa = ulTarifa;
                        vehLocal.TarifaOriginal = ulTarifaOriginal;
                        vehLocal.TipoTarifa = btTipoTarifa;

                        _logger.Info("AsignarTag -> Tag habilitado, asigno forma de pago. TipOp [{0}] TipBo[{1}] InfoTag.TipOp[{2}] InfoTag.TipBo[{3}]", vehLocal.TipOp, vehLocal.TipBo, vehLocal.InfoTag.TipOp, vehLocal.InfoTag.TipBo);
                        //Determino y asigno la forma de pago del tag	
                        //@@VIS: Ahora hay Chip
                        if (vehLocal.TipOp == 'T')
                        {
                            switch (vehLocal.TipBo)
                            {
                                case 'P': vehLocal.FormaPago = eFormaPago.CTTPrepago; _logger.Info("AsignarTag -> PREPAGO"); break;
                                case 'C': vehLocal.FormaPago = eFormaPago.CTTCC; _logger.Info("AsignarTag -> POSPAGO"); break;
                                case 'A': vehLocal.FormaPago = eFormaPago.CTTAbono; _logger.Info("AsignarTag -> ABONO"); break;
                                case 'X': vehLocal.FormaPago = eFormaPago.CTTExen; _logger.Info("AsignarTag -> EXENTO"); break;
                                case 'T': vehLocal.FormaPago = eFormaPago.CTTTransporte; _logger.Info("AsignarTag -> TRANSPORTE"); break;
                                case 'B': vehLocal.FormaPago = eFormaPago.CTTOmnibus; _logger.Info("AsignarTag -> OMNIBUS"); break;
                                //case 'U': GetVehiculo(eVehiculo).SetFormaPago(CTTUFRE);		MensajeLab("AsignarTag -> UFRE");	break;
                                //case 'F': GetVehiculo(eVehiculo).SetFormaPago(CTTFederado);		MensajeLab("AsignarTag -> FEDERADO");	break;
                                case 'U': vehLocal.TransitoUFRE = true; vehLocal.EsperaRecargaVia = true; _logger.Info("AsignarTag -> UFRE"); break;
                                case 'F': vehLocal.Federado = true; _logger.Info("AsignarTag -> FEDERADO"); break;
                            }

                            if (oTag.PagoEnViaPrepago)
                            {
                                vehLocal.FormaPago = eFormaPago.CTTPrepago;
                                vehLocal.EsperaRecargaVia = false;
                                _logger.Info("AsignarTag -> PREPAGO, ES PAGO EN VIA PREPAGO");
                            }
                            else if (vehLocal.EsperaRecargaVia && !vehLocal.TransitoUFRE)
                                vehLocal.EsperaRecargaVia = false;
                        }
                        else if (vehLocal.TipOp == 'C' && !vehLocal.ListaRecarga.Any(x => x != null))
                            vehLocal.EsperaRecargaVia = true;
                        else
                        {
                            switch (vehLocal.TipBo)
                            {
                                case 'P': vehLocal.FormaPago = eFormaPago.CTChPrepago; _logger.Info("AsignarTag -> PREPAGO"); break;
                                case 'C': vehLocal.FormaPago = eFormaPago.CTTCC; _logger.Info("AsignarTag -> POSPAGO"); break;
                                case 'A': vehLocal.FormaPago = eFormaPago.CTTAbono; _logger.Info("AsignarTag -> ABONO"); break;
                                case 'X': vehLocal.FormaPago = eFormaPago.CTChExen; _logger.Info("AsignarTag -> EXENTO"); break;
                                case 'T': vehLocal.FormaPago = eFormaPago.CTChTransporte; _logger.Info("AsignarTag -> TRANSPORTE"); break;
                                case 'B': vehLocal.FormaPago = eFormaPago.CTChOmnibus; _logger.Info("AsignarTag -> OMNIBUS"); break;
                                //case 'U': GetVehiculo(eVehiculo).SetFormaPago(CTChUFRE);		MensajeLab("AsignarTag -> UFRE");	break;
                                //case 'F': GetVehiculo(eVehiculo).SetFormaPago(CTChFederado);		MensajeLab("AsignarTag -> FEDERADO");	break;
                                case 'U': vehLocal.TransitoUFRE = true; vehLocal.EsperaRecargaVia = true; _logger.Info("AsignarTag -> UFRE"); break;
                                case 'F': vehLocal.Federado = true; _logger.Info("AsignarTag -> FEDERADO"); break;
                            }

                            if (vehLocal.EsperaRecargaVia && !vehLocal.TransitoUFRE)
                                vehLocal.EsperaRecargaVia = false;
                        }

                        //Si la vía está en modo D y el tag esta habilitado
                        //la categoría del vehículo es la autotabulante
                        if (_logicaCobro.Modo.Modo == "D" && vehLocal.TipOp != 'C')
                            vehLocal.Categoria = oTag.Categoria;

                        _logger.Info("AsignarTag -> Fin T_Vehiculo:[{Name}] Tag:[{Name}]", vehiculo.ToString(), oTag.NumeroTag);

                        //Sumo el transito Pagado en Turno
                        if (!vehLocal.EsperaRecargaVia) //si no es un tag sin saldo o pago en via lo sumo
                            TieneFormaPago = true;

                        if (vehLocal.TipOp == 'O')
                            vehLocal.LecturaPorOCR = true;
                    }
                    else
                    {
                        if(vehLocal.TipOp == 'O')
                            vehLocal.LecturaPorOCR = false;
                        _logger.Info("AsignarTag -> No asigno Forma de Pago");
                    }

                    int i = (int)eVehiculo.eVehP3;
                    int total = _vVehiculo.GetLength(0);
                    Vehiculo oVeh = new Vehiculo();

                    _logger.Info("AsignarTag -> Buscamos vehiculo. ulNroVehiculo[{0}] T_Vehiculo:[{1}]", ulNroVehiculo, vehiculo.ToString());

                    //no enviaron nro de vehiculo utiliza el enum
                    if (ulNroVehiculo == 0)
                    {
                        oVeh = GetVehiculo(vehiculo);
                        oVeh.CopiarVehiculo(ref vehLocal);
                        _logger.Info("AsignarTag-> Tag asignado. Vehiculo [{0}]", vehiculo.ToString());
                    }
                    else
                    {
                        bool bEnc = false;
                        //Vuelvo a buscar en la lista de vehículos el vehículo con número ulNroVehiculo por si se movió
                        while (i < total)
                        {
                            oVeh = GetVehiculo((eVehiculo)i);

                            if (oVeh.NumeroVehiculo == vehLocal.NumeroVehiculo)
                            {
                                bEnc = true;
                                oVeh.CopiarVehiculo(ref vehLocal);
                                oVeh.BarreraAbierta = false; //limpiamos flag porque ya se asignó el tag al veh
                                _logger.Info("AsignarTag-> Tag asignado. Vehiculo [{0}] - Nro [{1}]", (eVehiculo)i, oVeh.NumeroVehiculo);
                                break;
                            }
                            i++;
                        }

                        //Si no lo encuentra, algo pasó con el nro de veh (se limpió), utilizamos enum
                        if (!bEnc)
                        {
                            oVeh = GetVehiculo(vehiculo);
                            oVeh.CopiarVehiculo(ref vehLocal);
                            _logger.Info("AsignarTag-> Encontramos veh, Tag asignado. Vehiculo [{0}]", vehiculo.ToString());
                        }
                    }

                    if (oTag.GetTagHabilitado())
                    {
                        //Sumo el transito Pagado en Turno
                        if (TieneFormaPago) //si no es un tag sin saldo o pago en via lo sumo
                        {
                            TieneFormaPago = false;

                            if (oVeh.Categoria == 0)
                                oVeh.Categoria = oTag.Categoria;
                            else if (oVeh.Categoria != oTag.Categoria)
                            {
                                //verificar que se use la categoria del Tag
                                if (oTag.TipOp == 'T' || oTag.TipOp == 'O')
                                {
                                    oVeh.Categoria = oTag.Categoria;
                                    oVeh.CategoDescripcionLarga = oTag.CategoDescripcionLarga;
                                    oVeh.Tarifa = ulTarifa;
                                    oVeh.TipoDiaHora = sTipDH;
                                    oVeh.InfoTag.TipDH = sTipDH;
                                    oVeh.IVA = ulIVA;
                                }
                            }

                            //me fijo si hay un veh eliminado para no enviar el SetCobro otra vez
                            if (_vehiculoAEliminar.HayVehiculo &&
                                (!string.IsNullOrEmpty(_vehiculoAEliminar.NumeroTag) && !string.IsNullOrEmpty(oTag.NumeroTag) &&
                                (_vehiculoAEliminar.NumeroTag == oVeh.InfoTag.NumeroTag)))
                            {
                                oVeh.NumeroTransito = _vehiculoAEliminar.NumeroTransito;
                                oVeh.Fecha = _vehiculoAEliminar.Fecha;
                                _vehiculoAEliminar.Clear();
                            }
                            else
                            {
                                _logger.Debug("AsignarTag-> Almacena Pagado en Turno");

                                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(oVeh, _logicaCobro.GetTurno);

                                //Capturo foto y video
                                DecideCaptura(oTag.LecturaManual == 'S' ? eCausaVideo.Pagado : eCausaVideo.PagadoTelepeaje, oVeh.NumeroVehiculo);
                                //almacena la foto
                                ModuloFoto.Instance.AlmacenarFoto(ref oVeh);
                                //Se incrementa tránsito si no tenía nro y se envía evento de Cobro
                                if (oVeh.NumeroTransito == 0)
                                    oVeh.NumeroTransito = IncrementoTransito();

                                oVeh.Operacion = "CB";
                                ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, oVeh);
                            }
                        }
                        else
                            _logger.Debug("AsignarTag-> es tag sin saldo o pago en via, no lo sumo");
                    }
                    else
                    {
                        if (oTag.ErrorTag == eErrorTag.SinSaldo)
                            oVeh.EsperaRecargaVia = true;

                        _logger.Debug("AsignarTag-> {0} no habilitado, errorTag[{1}]", oTag.NumeroTag, oTag.ErrorTag.ToString());
                    }
                }
                else
                {
                    _logger.Info("AsignarTag-> T_Vehiculo[{Name}] ya tiene forma de pago asignada [{Name}], no asigno Tag y genero evento TagSinVeh", vehiculo.ToString(), vehLocal.FormaPago);
                    // Evento de tag sin vehiculo con codigo veh con varios Tags
                    ModuloEventos.Instance.SetVehiculoSinTag(_logicaCobro.GetTurno, vehLocal, 0, '1');
                }
            }
            bAsignandoTagAVehiculo = false;
            _logger.Info("AsignarTag -> Salgo");
            return vehiculo;
        }

        public override void LimpiarTagsLeidos()
        {
            _lstTagLeidoAntena.Clear();
            _lstInfoTagLeido.Clear();
            SetMismoTagIpico();
            ModuloAntena.Instance.LimpiarListaTagsAntiguos();
            _vehiculoAEliminar.Clear();
        }

        /// <summary>
        /// Elimina de la lista de tags leidos un tag por no ser más válido
        /// Genera una operacion cerrada de ese tag
        /// Utiliza eVehTra como vehiculo auxiliar sobre el que trabajar
        /// NO DEBE USARSE EN MODO M o MD
        /// </summary>
        /// <param name="oInfoTag">Datos del tag leido</param>
        /// <returns>true si pudo sacar el elemento de la lista, false en caso contrario</returns>
        private bool LimpiarTagLeido(InfoTag oInfoTag)
        {
            bool bRet = false;

            _logger.Trace("LimpiarTagLeido -> Tag:[{Name}]", oInfoTag.NumeroTag);

            //Si está habilitado y no es PAgoEnVia debo generar un transito
            if (oInfoTag.GetTagHabilitado() && oInfoTag.TipoTag != eTipoCuentaTag.Ufre)
            {
                if (ModuloAntena.Instance.AntenaNoRecibeMasLecturas(oInfoTag.NumeroTag))
                {
                    _logger.Info("LimpiarTagLeido -> Tag:[{Name}] estaba habilitado", oInfoTag.NumeroTag);

                    //Elimina el tag de la lista de tags leidos
                    bRet = DepartTagLeido(oInfoTag.NumeroTag);
                    //Borro el tag leido por antena
                    SetMismoTagIpico(oInfoTag.NumeroTag);

                    //limpiamos VehTra antes de usarlo
                    _vVehiculo[(int)eVehiculo.eVehTra] = new Vehiculo();
                    Vehiculo oVehiculo = GetVehiculo(eVehiculo.eVehTra);
                    oVehiculo.Ocupado = true;
                    //Copio la foto
                    oVehiculo.ListaInfoFoto.Add(oInfoTag.InfoMedios);

                    //Asignamos a eVehAnt como vehiculo auxiliar para enviar AB
                    AsignarTag(oInfoTag, 0, eVehiculo.eVehTra);

                    //Finalizo los videos que estaban grabando
                    ModuloVideo.Instance.DetenerVideo(oVehiculo, eCausaVideo.Nada, eCamara.Lateral);
                    int index = oVehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
                    if (index != -1)
                        oVehiculo.ListaInfoVideo[index].EstaFilmando = false;

                    CausaCancelacion causa = new CausaCancelacion();

                    causa.Codigo = "99";
                    causa.Descripcion = "Tag Sin Vehiculo";

                    _logger.Info("LimpiarTagLeido -> Generar transito AB. Vehiculo:[{Name}] ", eVehiculo.eVehTra);
                    //Envío VehTra
                    _logicaCobro.Cancelacion(causa, true, 0, eVehiculo.eVehTra);
                }
            }
            else
            {
                _logger.Info("LimpiarTagLeido -> Tag:[{Name}] no estaba habilitado", oInfoTag.NumeroTag);

                //Elimina el tag de la lista de tags leidos
                bRet = DepartTagLeido(oInfoTag.NumeroTag);
                //Borro el tag leido por antena
                SetMismoTagIpico(oInfoTag.NumeroTag);

                Vehiculo veh = new Vehiculo();

                // Evento de tag sin vehiculo
                if (!string.IsNullOrEmpty(oInfoTag.NumeroTag))
                {
                    _logger.Info("LimpiarTagLeido -> Tag sin vehiculo [{Name}]", oInfoTag.NumeroTag);
                    veh.InfoTag = oInfoTag;
                    ModuloEventos.Instance.SetVehiculoSinTag(_logicaCobro.GetTurno, veh, 0, '0');
                }
            }

            return bRet;
        }

        /// <summary>
        /// Saca de la lista de tags leidos el que tenga como número de
        /// tag a sNumeroTag
        /// </summary>
        /// <param name="sNumeroTag">Número de tag a quitar</param>
        /// <returns> true si pudo sacar el elemento de la lista, false en caso contrario</returns>
        private bool DepartTagLeido(string sNumeroTag)
        {
            bool bEnc = false;
            _logger.Info("DepartTagLeido -> Inicio Tag:[{Name}]", sNumeroTag);
            lock (_lockModificarListaTag)
            {
                int pos = _lstInfoTagLeido.Count;
                int i = 0;
                while (!bEnc && i < pos)
                {
                    if (_lstInfoTagLeido[i].GetInfoTag().NumeroTag == sNumeroTag ||
                        _lstInfoTagLeido[i].GetInfoTag().NumeroTag.Contains(sNumeroTag))
                    {
                        //Encontré el elemento, lo saco
                        bEnc = true;
                        _lstInfoTagLeido.RemoveAt(i);
                        i = 0;
                        _logger.Info("DepartTagLeido -> Remuevo Tag:[{Name}]", sNumeroTag);
                    }
                    else
                    {
                        //Paso al siguiente elemento
                        i++;
                    }
                }
                //Salvamos la nueva lista
                GrabarTagsLeidos();

            }
            _logger.Info("DepartTagLeido -> Fin");
            return bEnc;
        }

        private void GrabarTagsLeidos()
        {
            //TODO Implementar
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
            InfoTagLeido oInfoTagLeido = null;
            bool bSendLogica = false;
            ulong ulVehiculoLimite = 0;

            _logger.Info("******************* InicioColaOff -> Ingreso");
            try
            {
                try
                {
                    if (RespDAC > 0)
                        respuestaDAC = Convert.ToChar(RespDAC);
                    else
                        respuestaDAC = 'D';
                }
                catch
                {
                    respuestaDAC = 'D';
                }

                if (respuestaDAC == '2' || respuestaDAC == 'C')
                    ulVehiculoLimite = GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo;
                else if (respuestaDAC == '1')
                    ulVehiculoLimite = GetVehiculo(eVehiculo.eVehC1).NumeroVehiculo;
                else
                    ulVehiculoLimite = 0;

                _logger.Info("OnInicioColaOFF -> Inicio RetDAC:[{Hex}] (HEX) [{Name}] Vehiculo [{Name}] InfoTagLeidoCount [{name}]", respuestaDAC, respuestaDAC, ulVehiculoLimite, _lstInfoTagLeido.Count());

                //Para cada uno de los elementos de la lista de TagsLeidos quito el vehículo
                //con número más alto
                int pos = 0;
                int ultimo = _lstInfoTagLeido.Count;
                try
                {
                    while (pos < ultimo)
                    {
                        oInfoTagLeido = _lstInfoTagLeido[pos];
                        //Nos fijamos el ultimo vehiculo que va a quedar

                        //Borro el vehiculo mas adelantado, salvo que el mas atrasado va a ser sacado de la via
                        oInfoTagLeido.BorrarVehiculoMenor(ulVehiculoLimite);

                        /*
                        //TODO Falta esto mismo en el que estamos leyendo
                        //un MA Px pasa cuando era una falsa deteccion de otro vehiculo 
                        // o al final de un retroceso real
                        //En la falsa deteccion quiero que el tag se lo quede el vehiculo real
                        //En un retroceso real ya quito al de adelante en el C->P Quitar
                        if( lRetDAC == '1' || lRetDAC == '2' )
                            oInfoTagLeido.BorrarVehiculoMayor();
                        else
                            oInfoTagLeido.BorrarVehiculoMenor();
                            */

                        //Si queda un solo elemento en los vehículos del tag y tiene tag leído llamo a AsigarTagAVehiculo
                        if (oInfoTagLeido.GetCantVehiculos() == 1 && oInfoTagLeido.GetInfoTag().GetInit())
                        {
                            if (AsignarTagAVehiculo(oInfoTagLeido.GetInfoTag(),
                                                     oInfoTagLeido.GetNumeroVehiculo(0)))
                            {
                                //Comienzo nuevamente desde el principio porque AsignarTagAVehiculo
                                //sacó el elemento de la lista
                                pos = 0;
                                //si ya no hay mas elementos en la lista me salgo
                                if (_lstInfoTagLeido.Count == 0)
                                {
                                    _logger.Info("OnInicioColaOFF -> La lista quedó vacia");
                                    break;
                                }
                            }
                            else
                            {
                                //Avanzo al siguiente elemento
                                pos++;
                            }
                        }
                        else
                        {
                            //Avanzo al siguiente elemento
                            pos++;
                        }
                    }
                }
                catch
                {
                    _logger.Info("OnInicioColaOFF -> Error al quitar vehiculo [{0}]", pos);
                }

                //Tambien saco vehiculos del tag que estamos leyendo
                _infoTagLeido.BorrarVehiculoMenor(ulVehiculoLimite);

                //Este control se debe hacer antes que nada
                if (respuestaDAC == '0' && _logicaCobro.Estado != eEstadoVia.EVCerrada)
                {
                    //Estado normal P1 ocupado, P2 puede o no estar ocupado y resto libre . 
                    //Si no es este estado genero un evento de falla crítica y log de sensores
                    if (!(!GetVehiculo(eVehiculo.eVehP3).Ocupado &&
                          GetVehiculo(eVehiculo.eVehP1).Ocupado && !GetVehiculo(eVehiculo.eVehC1).Ocupado &&
                          !GetVehiculo(eVehiculo.eVehC0).Ocupado))
                    {
                        if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                            bSendLogica = true;
                    }
                }

                bool VariosVehiculosEnFila = false;

                //JAS 20090731
                //Si la barrera está abierta
                //Buscamos un vehiculo sin tag para sacar
                if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
                {
                    LoguearColaVehiculos();
                    int vehOcupados = 0;
                    for (int i = (int)eVehiculo.eVehP3; i <= (int)eVehiculo.eVehC0; i++)
                    {
                        if (_vVehiculo[i].Ocupado)
                            vehOcupados++;
                    }

                    VariosVehiculosEnFila = vehOcupados > 1;

                    // Tambien contaba los Observado, Video, Online, etc.
                    //VariosVehiculosEnFila = _vVehiculo.Where(x => x.Ocupado).Count() > 1 ? true : false;

                    if (VariosVehiculosEnFila)
                    {
                        eVehiculo eIndexPrimero = eVehiculo.eVehP3, eIndex = eVehiculo.eVehP3, eIndexFound = eVehiculo.eVehP3;
                        if (respuestaDAC == '2')
                            eIndexPrimero = eVehiculo.eVehP2;
                        else if (respuestaDAC == '1' || respuestaDAC == 'F')
                            eIndexPrimero = eVehiculo.eVehP1;

                        eIndexFound = eIndexPrimero;
                        //Buscamos el primero sin tag ni forma de pago
                        for (int i = (int)eIndexPrimero; i <= (int)eVehiculo.eVehC0; i++)
                        {
                            eIndex = (eVehiculo)i;
                            if (GetVehiculo(eIndex).InfoTag.NumeroTag == ""
                                && !GetVehiculo(eIndex).EstaPagado
                                && GetVehiculo(eIndex).Ocupado)
                            {
                                eIndexFound = eIndex;
                                break;
                            }
                        }

                        _logger.Info("OnInicioColaOFF->Barrera Abierta [{Name}] Primero [{Name}] Sin Tag[{Name}]", respuestaDAC, eIndexPrimero, eIndexFound);

                        //Tenemos que sacar los menores a eIndexPrimero y eIndexFound
                        if (eIndexPrimero > eVehiculo.eVehP3 || eIndexFound == eVehiculo.eVehP3)
                            OpCerradaReversa(eVehiculo.eVehP3);
                        //Tenemos que sacar los menores a eIndexPrimero y eIndexFound
                        if (eIndexPrimero > eVehiculo.eVehP2 || eIndexFound == eVehiculo.eVehP2)
                            OpCerradaReversa(eVehiculo.eVehP2);
                        else
                        {
                            if (GetVehiculo(eVehiculo.eVehP2).Categoria == 0 && !GetVehiculo(eVehiculo.eVehP2).EstaPagado)
                            {
                                _logger.Info("OnInicioColaOFF -> Limpiamos ocupado. eVehP2 - Nro.[{Name}]", GetVehiculo(eVehiculo.eVehP2).NumeroVehiculo);
                                GetVehiculo(eVehiculo.eVehP2).NumeroVehiculo = 0;
                                GetVehiculo(eVehiculo.eVehP2).Ocupado = false;
                            }
                        }
                        //Tenemos que sacar los menores a eIndexPrimero y eIndexFound
                        if (eIndexPrimero > eVehiculo.eVehP1 || eIndexFound == eVehiculo.eVehP1)
                            OpCerradaReversa(eVehiculo.eVehP1);
                        else
                        {
                            if (GetVehiculo(eVehiculo.eVehP1).Categoria == 0 && !GetVehiculo(eVehiculo.eVehP1).EstaPagado)
                            {
                                _logger.Info("OnInicioColaOFF -> Limpiamos ocupado. eVehP1 - Nro.[{Name}]", GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo);
                                GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo = 0;
                                GetVehiculo(eVehiculo.eVehP1).Ocupado = false;
                            }
                        }
                        //Tenemos que sacar los menores a eIndexPrimero y eIndexFound
                        if (eIndexPrimero > eVehiculo.eVehC1 || eIndexFound == eVehiculo.eVehC1)
                            OpCerradaReversa(eVehiculo.eVehC1);
                        else
                        {
                            if (GetVehiculo(eVehiculo.eVehC1).Categoria == 0 && !GetVehiculo(eVehiculo.eVehC1).EstaPagado)
                            {
                                _logger.Info("OnInicioColaOFF -> Limpiamos ocupado. eVehC1 - Nro.[{Name}]", GetVehiculo(eVehiculo.eVehC1).NumeroVehiculo);
                                GetVehiculo(eVehiculo.eVehC1).NumeroVehiculo = 0;
                                GetVehiculo(eVehiculo.eVehC1).Ocupado = false;
                            }
                        }
                        //Tenemos que sacar los menores a eIndexPrimero y eIndexFound
                        if (eIndexPrimero > eVehiculo.eVehC0 || eIndexFound == eVehiculo.eVehC0)
                            OpCerradaReversa(eVehiculo.eVehC0);
                        else
                        {
                            if (GetVehiculo(eVehiculo.eVehC0).Categoria == 0 && !GetVehiculo(eVehiculo.eVehC0).EstaPagado)
                            {
                                _logger.Info("OnInicioColaOFF -> Limpiamos ocupado. eVehC0 - Nro.[{Name}]", GetVehiculo(eVehiculo.eVehC0).NumeroVehiculo);
                                GetVehiculo(eVehiculo.eVehC0).NumeroVehiculo = 0;
                                GetVehiculo(eVehiculo.eVehC0).Ocupado = false;
                            }
                        }

                        //Si sacamos uno distinto al primero debemos adelantar a los demas, revisar si se le asignó un tag antes de mover
                        if (eIndexPrimero < eIndexFound &&
                            (GetVehiculo(eIndexFound).InfoTag.NumeroTag == "" &&
                             !GetVehiculo(eIndexFound).EstaPagado))
                        {
                            _logger.Debug("InicioColaOff -> Muevo la fila hacia adelante");
                            LoguearColaVehiculos();
                            //Paso los vehículos hacia adelante empezando desde atrás (desde eIndexFound a eIndexPrimero)
                            for (int f = (int)eIndexFound; f > (int)eIndexPrimero; f--)
                                _vVehiculo[f] = _vVehiculo[f - 1];

                            //Eliminamos el eIndexPrimero
                            _vVehiculo[(int)eIndexPrimero] = new Vehiculo();
                            LoguearColaVehiculos();
                        }
                    }
                }

                if (respuestaDAC != 'F' && !VariosVehiculosEnFila)
                {
                    //Barrera cerrada sacamos el de más atras
                    //Retroceder vehículo 3	(siempre)
                    OpCerradaReversa(eVehiculo.eVehP3);

                    //Retroceder vehículo 2
                    if (respuestaDAC == '2' || respuestaDAC == '1' || respuestaDAC == '0' || respuestaDAC == 'C')
                        OpCerradaReversa(eVehiculo.eVehP2);

                    //Retroceder vehículo 1
                    if (respuestaDAC == '1' || respuestaDAC == '0')
                        OpCerradaReversa(eVehiculo.eVehP1);
                }


                if (respuestaDAC == '0')
                {
                    //Sacamos hacia atras los de la cola
                    //Solo se genera evento si leyo pase
                    OpCerradaReversa(eVehiculo.eVehC1, true);
                    OpCerradaReversa(eVehiculo.eVehC0, true);

                    //Llamo a SetVehiculoIng() para que actualice el display
                    //Si queda la via libre no actualizo monitor
                    SetVehiculoIng(false, true);
                }

                if (respuestaDAC == 'F')
                {
                    //Sacamos hacia atras los de la cola
                    //Retroceder vehículo 3	(siempre)
                    OpCerradaReversaSiNoPagado(eVehiculo.eVehP3, true);
                    //Retroceder vehículo 2
                    OpCerradaReversaSiNoPagado(eVehiculo.eVehP2, true);
                    //Retroceder vehículo 1
                    OpCerradaReversaSiNoPagado(eVehiculo.eVehP1, true);
                    //Sacamos hacia atras los de la cola
                    OpCerradaReversaSiNoPagado(eVehiculo.eVehC1, true);
                    OpCerradaReversaSiNoPagado(eVehiculo.eVehC0, true);

                    //Llamo a SetVehiculoIng() para que actualice el display
                    //Si queda la via libre no actualizo monitor
                    SetVehiculoIng(false, true);
                }

                //Retrocedo todos los vehículos desde C0 hasta P2
                if (respuestaDAC == 'C')
                {
                    //Si no estamos en modo autista
                    if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                    {
                        //Si C1 está ocupado genero un evento de marcha atrás
                        if (GetVehiculo(eVehiculo.eVehC0).NoVacio)
                        {
                            Vehiculo oVehiculo = GetVehiculo(eVehiculo.eVehC0);
                            EnviarEventoMarchaAtras(ref oVehiculo, /*m_Fecha*/DateTime.Now, oVehiculo.Categoria, "MA");
                        }
                    }

                    RetrocederVehiculosModoD();
                }

                GrabarVehiculos();

                SetVehiculoIng(false, false);
                //Genero un evento para cada uno de los sensores que fallaron
                //Si no hubo falla de sensores mando las fallas de lógica que
                //se produjeron (se envian dentro del método GrabarLogSensores)
                //Si no está la vía en sentido opuesto
                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                {
                    if (!FallaSensoresDAC())
                    {
                        if (bSendLogica)
                        {
                            GrabarLogSensores("OnInicioColaOFF", eLogSensores.Logica_Sensores);
                        }
                    }
                }

                RegularizarFilaVehiculos();
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            LoguearColaVehiculos();
            _logger.Info("******************* InicioColaOff -> Salida");
        }


        /// <summary>
        /// Genera un evento de marcha atrás
        /// </summary>
        /// <param name="oVehiculo">referencia al vehículo que hizo marcha atrás</param>
        /// <param name="fecha">fecha del evento</param>
        /// <param name="catego">categoría del tránsito</param>
        /// <param name="tipoRetroceso">tipo de retroceso (MA o AT)</param>
        public void EnviarEventoMarchaAtras(ref Vehiculo oVehiculo, DateTime fecha,
                                               short catego, string tipoRetroceso)
        {
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

                //Salvamos la ultima huella
                _huellaDAC = oVehiculo.Huella;
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }


        /// <summary>
        /// Retrocede los vehículos desde C0 hasta P2 dejando libre C0
        /// </summary>
        private void RetrocederVehiculosModoD()
        {
            int i = 0;
            _logger.Debug("RetrocederVehiculosModoD -> Muevo la fila hacia atras");
            LoguearColaVehiculos();
            //Paso los vehículos hacia atrás empezando desde adelante (desde eVehP3 a eVehC0)
            for (i = (int)eVehiculo.eVehP3; i < (int)eVehiculo.eVehC0; i++)
            {
                _vVehiculo[i] = _vVehiculo[i + 1];
                //Le limpio el SalidaOn
                _vVehiculo[i].SalidaON = false;
            }

            //Limpio C0
            _vVehiculo[(int)eVehiculo.eVehC0] = new Vehiculo();
            _vVehiculo[(int)eVehiculo.eVehP1].VehEntro = false;

            LoguearColaVehiculos();
            _logger.Debug("RetrocederVehiculosModoD -> Salgo");
        }

        public override void AdelantarFilaVehiculosDesde(eVehiculo inicio)
        {
            int i = 0;
            _logger.Debug("AdelantarFilaVehiculosDesde -> [{0}]", inicio.ToString());
            LoguearColaVehiculos();
            //Paso los vehículos hacia adelante
            for (i = (int)inicio; i > (int)eVehiculo.eVehP3; i--)
            {
                _vVehiculo[i] = _vVehiculo[i - 1];
            }

            _vVehiculo[(int)eVehiculo.eVehP3] = new Vehiculo();

            LoguearColaVehiculos();
            _logger.Debug("AdelantarFilaVehiculosDesde -> Salgo");
        }

        private void SetVehiculo(eVehiculo eVeh, Vehiculo oVehiculo)
        {
            _vVehiculo[(byte)eVeh] = oVehiculo;
        }

        /// <summary>
        ///  Si el vehículo está ocupado y tiene forma de pago genera un evento 
        /// de operación cerrada por marcha atrás del vehículo eVehiculo
        /// y limpia el contenido del mismo
        /// </summary>
        /// <param name="eVehiculo">vehículo a retroceder</param>
        /// <param name="bFalla">si es true solo generamos evento si leyo pase. si es false (default) tambien si entro en sentido inverso a la via</param>
        /// <param name="bForzar">si es true enviar si o si el transito del vehiculo</param>
        private void OpCerradaReversa(eVehiculo eVehiculo, bool bFalla = false, bool bForzar = false)
        {
            _logger.Info("OpCerradaReversa -> inicio. Vehiculo[{Name}]", eVehiculo.ToString());
            Vehiculo oVehiculo = GetVehiculo(eVehiculo);

            OpCerradaReversa(ref oVehiculo, bFalla, bForzar);

            SetVehiculo(eVehiculo, oVehiculo);

            _logger.Info("OpCerradaReversa -> fin");
        }

        private void OpCerradaReversaSiNoPagado(eVehiculo eVehiculo, bool bFalla = false, bool bForzar = false)
        {
            _logger.Info("OpCerradaReversaSiNoPagado -> inicio. Vehiculo[{Name}]", eVehiculo.ToString());
            Vehiculo oVehiculo = GetVehiculo(eVehiculo);

            if (oVehiculo.EstaPagado || oVehiculo.TieneTag || oVehiculo.Categoria > 0)
            {
                if (eVehiculo == eVehiculo.eVehP1 || eVehiculo == eVehiculo.eVehP2 || eVehiculo == eVehiculo.eVehP3)
                {
                    GetVehiculo(eVehiculo).NumeroVehiculo = 0;
                    GetVehiculo(eVehiculo).Ocupado = false;
                }
                else
                {
                    //mover los datos a p1 dejando numero de vehiculo vacio
                    //si p1 ya estaba cobrado, primero muevo p1 a p2
                    //si p2 estaba pagado primero muevo p2 a p3, 
                    if (GetVehiculo(eVehiculo.eVehP1).EstaPagado)
                    {
                        if (GetVehiculo(eVehiculo.eVehP2).EstaPagado)
                        {
                            if (GetVehiculo(eVehiculo.eVehP3).EstaPagado)
                            {
                                Vehiculo veh = GetVehiculo(eVehiculo.eVehP3);
                                OpCerradaReversa(ref veh, bFalla, bForzar);
                            }
                            //Muevo P2 a P3
                            SetVehiculo(eVehiculo.eVehP3, GetVehiculo(eVehiculo.eVehP2));
                        }
                        //Muevo P1 a P2
                        SetVehiculo(eVehiculo.eVehP2, GetVehiculo(eVehiculo.eVehP1));
                    }
                    //Muevo vehiculo actual a P1
                    SetVehiculo(eVehiculo.eVehP1, oVehiculo);
                    //Borro numero de vehiculo a P1
                    GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo = 0;
                    GetVehiculo(eVehiculo.eVehP1).Ocupado = false;
                    //Borro vehiculo actual
                    SetVehiculo(eVehiculo, new Vehiculo());
                    _logger.Info("OpCerradaReversaSiNoPagado -> Borro Vehiculo {Name}", eVehiculo);
                }
            }
            else
            {
                OpCerradaReversa(ref oVehiculo, bFalla, bForzar);
                SetVehiculo(eVehiculo, oVehiculo);
            }

            _logger.Info("OpCerradaReversaSiNoPagado -> fin");
        }

        private void OpCerradaReversa(ref Vehiculo oVehiculo, bool bFalla = false, bool bForzar = false)
        {
            byte byEstado = 0;

            _logger.Info("OpCerradaReversa -> Inicio NumVeh:[{Name}] Tipop [{Name}] FP [{Name}]", oVehiculo.NumeroVehiculo, oVehiculo.TipOp, oVehiculo.FormaPago.ToString());
            ulong NumeroVehiculo = oVehiculo.NumeroVehiculo;

            //Si esta trabajando en el Sentido Opuesto o trabajo por ultima vez en sentido opuesto
            //y Estaba Cerrada
            if (_logicaCobro.Estado == eEstadoVia.EVCerrada && (IsSentidoOpuesto(false) || _ultimoSentidoEsOpuesto))
            {
                if (oVehiculo.IngresoSentidoContrario && !bFalla)
                {
                    ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionAutista);
                }
                _logger.Info("Limpio el Vehiculo");
                oVehiculo = new Vehiculo();
            }
            else if (oVehiculo.EstaPagado)
            {
                _logger.Info("OpCerradaReversa -> EstaPagado, limpiamos ocupado");
                oVehiculo.NumeroVehiculo = 0;
                oVehiculo.Ocupado = false;
                //Si el vehículo tiene tag habilitado permito leer el mismo tag
                if (!string.IsNullOrEmpty(oVehiculo.InfoTag.NumeroTag))
                {
                    //Recordamos el Tag Marcha Atras
                    _tagMarchaAtras = oVehiculo.InfoTag.NumeroTag;
                }
            }
            else if (!oVehiculo.EstaPagado && oVehiculo.Ocupado && bForzar)
            {
                _logger.Info("OpCerradaReversa -> Generar evento de violacion Reversa NumVeh:[{Name}]", oVehiculo.NumeroVehiculo);

                //Incremento y asigno el número de tránsito al vehículo
                if (oVehiculo.NumeroTransito == 0)
                    oVehiculo.NumeroTransito = IncrementoTransito();

                _logger.Info("OpCerradaReversa -> Generar evento de violacion Reversa NumTr:[{Name}]", oVehiculo.NumeroTransito);

                //Finalizo los videos que estaban grabando
                ModuloVideo.Instance.DetenerVideo(oVehiculo, eCausaVideo.Nada, eCamara.Lateral);
                int index = oVehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
                if (index != -1)
                    oVehiculo.ListaInfoVideo[index].EstaFilmando = false;

                oVehiculo.Reversa = true;
                //Si no tenia fecha
                if (oVehiculo.Fecha == DateTime.MinValue)
                    oVehiculo.Fecha = DateTime.Now;

                //Genero una Violacion
                oVehiculo.Operacion = "VI";
                _logger.Info("OpCerradaReversa->Violacion");

                //si no hay dac le damos la categoria autotabulante
                if (oVehiculo.InfoDac.Categoria == 0)
                    oVehiculo.InfoDac.Categoria = _catego_Autotabular;

                switch (_logicaCobro.Estado)
                {
                    case eEstadoVia.EVCerrada:
                        oVehiculo.TipoViolacion = 'R';
                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesPrevias);
                        break;

                    case eEstadoVia.EVQuiebreBarrera:
                        //Incremento el total de violaciones por quiebre de barrera
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

                if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera)
                {
                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.ViolQBL);
                }
                else if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                {
                    if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreControlado)
                    {
                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.ViolQBC);
                    }
                    else
                    {
                        ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.Viol);
                    }
                }

                bool activarAlarma = _logicaCobro.ModoQuiebre == eQuiebre.Nada ||
                                    (_logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.PermisoModo[ePermisosModos.AlarmaQuiebreLiberado]);
                //( _logicaCobro.ModoQuiebre != eQuiebre.Nada && ModuloBaseDatos.Instance.PermisoModo[ePermisosModos.AlarmaQuiebreLiberado]( (int)ePermisosModos.AlarmaQuiebreLiberado, _logicaCobro.Modo.Modo ) );

                if (activarAlarma)
                {
                    // ConfiguracionAlarma
                    ConfigAlarma oCfgAlarma = ModuloBaseDatos.Instance.BuscarConfiguracionAlarmaPrivate("V");
                    if (oCfgAlarma != null)
                    {
                        DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, oCfgAlarma.DuracionVisual, oCfgAlarma.DuracionSonido, true);
                        IniciarTimerApagadoCampanaPantalla(2000);

                        // Actualiza el estado de los mimicos en pantalla
                        Mimicos mimicos = new Mimicos();
                        mimicos.CampanaViolacion = enmEstadoAlarma.Activa;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                    }
                }
                DecideAlmacenar(eAlmacenaMedio.Violacion, ref oVehiculo);
                _estadoAbort = 'N';        //Operacion Cerrada
                EnviarEventoVehiculo(ref oVehiculo);
                oVehiculo = new Vehiculo();
            }
            else
            {
                _logger.Info("OpCerradaReversa -> Limpiamos ocupado:[{Name}]", oVehiculo.NumeroVehiculo);
                oVehiculo.NumeroVehiculo = 0;
                oVehiculo.Ocupado = false;
            }
            _logger.Info("OpCerradaReversa -> Fin NumVeh:[{Name}]", NumeroVehiculo);
        }

        private void LimpiarVehiculo(Vehiculo veh, eVehiculo eVeh)
        {
            if (!veh.EstaPagadoManual)
            {
                // Generar abortada
                if (veh.EstaPagado && (veh.TipOp == 'T' || veh.TipOp == 'O'))
                {
                    CausaCancelacion causa = new CausaCancelacion();

                    causa.Codigo = "99";
                    causa.Descripcion = "Tag Sin Vehiculo";

                    _logger.Info("LimpiarVehiculo -> Abortada Vehiculo {Name} - Nro. {Name}", eVeh, veh.NumeroVehiculo);
                    //Envío nro de vehículo y el enumerado, si tuviera nro 0 busca por el enumerado
                    _logicaCobro.Cancelacion(causa, true, veh.NumeroVehiculo, eVeh);
                }
                else if (!veh.CobroEnCurso)
                {
                    // Evento de tag sin vehiculo
                    if (!string.IsNullOrEmpty(veh.InfoTag.NumeroTag))
                    {
                        _logger.Info("LimpiarVehiculo -> Tag Sin Vehiculo. Nro. {Name} - T_Veh {Name} - Tag {Name}", veh.NumeroVehiculo, eVeh, veh.InfoTag.NumeroTag);
                        ModuloEventos.Instance.SetVehiculoSinTag(_logicaCobro.GetTurno, veh, 0, '0');

                        if (GetPrimerVehiculo()?.InfoTag.NumeroTag == veh.InfoTag.NumeroTag)
                        {
                            ModuloPantalla.Instance.LimpiarVehiculo(new Vehiculo());
                            ModuloPantalla.Instance.LimpiarMensajes();
                            // Se envia mensaje al display
                            ModuloDisplay.Instance.Enviar(eDisplay.BNV);
                        }
                    }
                    veh.InfoTag.Clear();
                    veh.ClearDatosSeteadosPorTag();
                }
            }
        }

        /// <summary>
        /// Genera un evento con el vehículo pVehiculo
        /// </summary>
        /// <param name="oVehiculo">Vehiculo a Enviar</param>
        private void EnviarEventoVehiculo(ref Vehiculo oVehiculo)
        {
            _logger.Info("EnviarEventoVehiculo Veh nro. [{0}] - Transito [{1}]", oVehiculo.NumeroVehiculo, oVehiculo.NumeroTransito);
            //Envio el evento ANT si está ocupado
            EnviarEventoVehAnt();

            //Asigno el vehículo eVehiculo a ANT y mando el evento
            LoguearColaVehiculos();
            _logger.Debug("EnviarEventoVehiculo -> Asigno vehiculo a VehAnt");
            _vVehiculo[(int)eVehiculo.eVehAnt] = oVehiculo;
            LoguearColaVehiculos();
            //Envio el evento ANT
            EnviarEventoVehAnt();
        }

        /// <summary>
        /// Si el vehículo ANT tiene un vehiculo válido genero un evento
        /// correspondiente con ese vehículo y vacío el dato
        /// </summary>
        private void EnviarEventoVehAnt()
        {
            //no enviar el VehAnt duplicado (en caso de que se quiera enviar eve de otro veh)
            if (GetVehAnt().Init && !GetVehAnt().GenerandoTransito)
                GeneraEventosTransito();
        }

        /// <summary>
        /// 
        /// </summary>
        private void GeneraEventosTransito()
        {
            _logger.Info("Genero EventoTransito -> Inicio. NroTra [{0}]", GetVehAnt().NumeroTransito);

            GetVehAnt().GenerandoTransito = true; //flag para marcar que está generando evento del vehAnt

            //Si no tiene asignado tipo transito
            if (GetVehAnt().Operacion == "" || GetVehAnt().Operacion == "CB")
            {
                if (GetVehAnt().EstaPagado)
                    GetVehAnt().Operacion = "TR";
                else
                {
                    GetVehAnt().Operacion = "VI";
                }
            }

            if (GetVehAnt().Operacion == "VI")
            {
                _loggerTransitos?.Info($"V;{DateTime.Now.ToString("HH:mm:ss.ff")};{GetVehAnt().Categoria};{GetVehAnt().TipOp};{GetVehAnt().TipBo};{GetVehAnt().GetSubFormaPago()};{GetVehAnt().InfoDac.Categoria};{GetVehAnt().NumeroVehiculo};{GetVehAnt().NumeroTransito}");
                ModuloEventos.Instance.SetViolacionXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, GetVehAnt());
            }
            else
            {
                _loggerTransitos?.Info($"T;{DateTime.Now.ToString("HH:mm:ss.ff")};{GetVehAnt().Categoria};{GetVehAnt().TipOp};{GetVehAnt().TipBo};{GetVehAnt().GetSubFormaPago()};{GetVehAnt().InfoDac.Categoria};{GetVehAnt().NumeroVehiculo};{GetVehAnt().NumeroTransito}");
                ModuloEventos.Instance.SetPasadaXML(ModuloBaseDatos.Instance.ConfigVia, _logicaCobro.GetTurno, GetVehAnt(),1);
                ModuloOCR.Instance.TransitoOCR(GetVehAnt());
            }
            _logicaCobro.FecUltimoTransito = DateTime.Now;

            //Copio al vehiculo para el video y online
            Vehiculo vehAnt = GetVehAnt();
            _vVehiculo[(int)eVehiculo.eVehVideo].CopiarVehiculo(ref vehAnt);

            //si hay recargas envio los eventos
            if (GetVehAnt().ListaRecarga.Any(x => x != null))
            {
                foreach (var recarga in GetVehAnt().ListaRecarga)
                {
                    if (recarga != null)
                    {
                        //Generacion del Evento de Recarga
                        ModuloEventos.Instance.SetRecargaFinal(_logicaCobro.GetTurno, recarga, ModuloBaseDatos.Instance.ConfigVia, GetVehAnt());
                    }
                }
                GetVehAnt().ListaRecarga.Clear();
            }

            _vVehiculo[(int)eVehiculo.eVehOnLine].CopiarVehiculo(ref _vVehiculo[(int)eVehiculo.eVehAnt]);
            _infoDACOnline = GetVehAnt().InfoDac;

            //Copio VehAnt a VehObservado
            _vVehiculo[(int)eVehiculo.eVehObservado].CopiarVehiculo(ref _vVehiculo[(int)eVehiculo.eVehAnt]);

            //Limpio el vehículo ANT
            _logger.Info("GenerarEventoTransito -> Limpio VehAnt");
            _vVehiculo[(int)eVehiculo.eVehAnt] = new Vehiculo();

            LoguearColaVehiculos();
        }


        private void DesRegistroPagoTag(ref Vehiculo oVehiculo) //ref InfoTag
        {

            ModuloBaseDatos.Instance.DesalmacenarPagadoTurnoAsync(oVehiculo, _logicaCobro.GetTurno);

        }

        void RegistroPagoTag(ref InfoTag oInfoTag, ref Vehiculo oVeh)
        {
            _logger.Info($"RegistroPagoTag Inicio - NroVehiculo[{oVeh.NumeroVehiculo}] Tag Prepago[{oInfoTag.EsPrepago()}]");

            //Descontamos el viaje
            if (oInfoTag.EsPrepago())
                oInfoTag.DescontarSaldo();
        }

        /// <summary>
        /// Adelanta los vehículos de la vía dinámica 1 posición y si C0
        /// tenía datos genera un evento de tránsito o violación con él
        /// </summary>
        public override void AdelantarVehiculosModoD()
        {
            int i = 0;
            _logger.Info("AdelantarVehiculosModoD -> Inicio");

            // Si el vehiculo 0 tiene una salida falsa
            // no muevo la cola y le quito la salida falsa
            // porque la via con esto queda igual al PIC
            if (GetVehiculo(eVehiculo.eVehC0).SalidaFalsa == true)
            {
                _logger.Info("AdelantarVehiculosModoD -> tenía Salida Falsa, sincronizamos");
                GetVehiculo(eVehiculo.eVehC0).ClearSalidaFalsa();
            }
            else
            {
                //Si C0 tiene falla de salida guardamos el video para el de atras
                if (GetVehiculo(eVehiculo.eVehC0).Ocupado
                    && GetVehiculo(eVehiculo.eVehC0).FallaSalida)
                {
                    _logger.Info("AdelantarVehiculosModoD->Salvamos video para el proximo");
                    _ultimoVideo = GetVehiculo(eVehiculo.eVehC0).ListaInfoVideo;
                }

                //si C0 está en medio de un cobro, asignamos los datos al de atras
                if (GetVehiculo(eVehiculo.eVehC0).Ocupado
                    && GetVehiculo(eVehiculo.eVehC0).CobroEnCurso && GetVehiculo(eVehiculo.eVehP1).ErrorDeLogica)
                {
                    // Si C1 no esta pagado copio veh C0 a C1 y C1 a C0
                    if (!GetVehiculo(eVehiculo.eVehC1).EstaPagadoManual)
                    {
                        _logger.Info("AdelantarVehiculosModoD -> C0 en medio de un cobro, movemos a C1");
                        Vehiculo vehAux = new Vehiculo();
                        Vehiculo vehC0 = GetVehiculo(eVehiculo.eVehC0);
                        Vehiculo vehC1 = GetVehiculo(eVehiculo.eVehC1);

                        vehAux.CopiarVehiculo(ref vehC0);
                        vehC0.CopiarVehiculo(ref vehC1);
                        vehC1.CopiarVehiculo(ref vehAux);
                    }
                }

                //Si C0 tiene datos genero un evento de transito
                GenerarEventoSiOcupado(eVehiculo.eVehC0);

                _logger.Info("AdelantarVehiculosModoD -> Muevo los vehículos hacia adelante");
                lock (_lockAsignarTag)
                {
                    //Paso los vehículos hacia adelante empezando desde atrás (desde eVehC0 a eVehP3)
                    for (i = (int)eVehiculo.eVehC0; i > (int)eVehiculo.eVehP3; i--)
                        _vVehiculo[i] = _vVehiculo[i - 1];
                }

                //Actualizamos tiempo de C1
                GetVehiculo(eVehiculo.eVehC1).UltimoMovimiento = DateTime.Now;
                //Marcamos que ya entro a la via
                GetVehiculo(eVehiculo.eVehC1).VehEntro = true;

                //Limpio P3
                //GetVehiculo(eVehiculo.eVehP3).Clear(false);
                _vVehiculo[(int)eVehiculo.eVehP3] = new Vehiculo();
                LoguearColaVehiculos();
            }
            _logger.Info("AdelantarVehiculosModoD -> Fin");
        }

        private bool IsSentidoOpuesto(bool bEsCritico)
        {
            return bEsCritico ? DAC_PlacaIO.Instance.EntradaBidi() : _sentidoOpuesto;
        }

        private void ActivarAntena(char resp, eCausaLecturaTag eCausaLecturaTag)
        {
            InfoTagLeido oInfoTagLeido = new InfoTagLeido();
            bool bActAntena = false, bReactivar = false, bYaTenemosTag = false;
            byte[] vVehiculos = new byte[4];
            eVehiculo eVehIni = 0, eVehFin = 0;
            byte iIndex = 0;

            _logger.Info("Entro a ActivarAntena[{Name}] Causa[{name}]", resp, eCausaLecturaTag);
            _timerDesactivarAntena.Stop();

            //Via en modo dinámico y abierta 
            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
            {
                //JMR
                //Si la lista no esta vacía
                if (_lstInfoTagLeido.Any(x => x != null))
                {
                    bool bAntenaLeeTag = false, bTagEnLista = false;
                    string sTag = _lstInfoTagLeido.Last().OInfoTag.NumeroTag;
                    bAntenaLeeTag = ModuloAntena.Instance.AntenaSigueLeyendoTag(sTag, _tiempoUltimaLecturaTag, ref bTagEnLista);
                    //Si el ultimo tag de _lstInfoTagLeido tiene 0 vehiculos, el tag está habilitado 
                    //y la antena sigue leyendo el tag o dejo de leerlo hace poco (config: TIEMPO_ULTIMA_LECTURA_TAG)
                    if (_lstInfoTagLeido.Last().GetCantVehiculos() == 0 && _lstInfoTagLeido.Last().GetInfoTag().TagOK && bAntenaLeeTag)
                    {
                        //Uso ese objeto en vez de m_InfoTagLeido
                        oInfoTagLeido = _lstInfoTagLeido.Last();
                        _logger.Info("ActivarAntena -> Usamos Ultimo Veh de la cola");
                        //No vamos a activar la antena
                        bYaTenemosTag = true;
                    }
                    else
                    {
                        //Si se dejó de leer hace mas tiempo, permitir leer el mismo tag. 
                        if (bTagEnLista && !bAntenaLeeTag)
                        {
                            _logger.Debug("ActivarAntena -> Avisamos a antena que puede leer tag [{0}] nuevamente", sTag);
                            ModuloAntena.Instance.LimpiarTagAntiguo(sTag);
                            ModuloAntena.Instance.BorrarTag(sTag);
                            SetMismoTagIpico(sTag);
                        }

                        //Sino usamos m_InfoTagLeido*/
                        oInfoTagLeido = _infoTagLeido;
                        _logger.Info("ActivarAntena -> Usamos m_InfoTagLeido, 0 Veh en la lista");
                    }
                }
                else
                {
                    //Si la lista esta vacía 
                    oInfoTagLeido = _infoTagLeido;
                    _logger.Info("ActivarAntena -> Usamos _infoTagLeido, _lstInfoTagLeido Vacía");
                }

                bActAntena = true;  //Activo la antena

                if (resp >= 0)
                {
                    switch (resp)
                    {
                        //case 0:		//SIP BPR
                        case '0':   //Reactivar Antena
                                    //(para los mismos tags)
                            _logger.Info("ActivarAntena -> Reactivar");
                            bActAntena = true;
                            _bMismoTag = true;
                            bReactivar = true;
                            break;
                        case '1':   //Asigno P1
                            _logger.Info("ActivarAntena -> Asignar P1");

                            AsignarNumeroVehiculo(eVehiculo.eVehP1);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            oInfoTagLeido.AgregarVehiculo(GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo);
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo;
                            break;

                        case '2':   //Asigno P1 y P2
                            _logger.Info("ActivarAntena -> Asignar P1 y P2");
                            vVehiculos[0] = (byte)eVehiculo.eVehP2;
                            vVehiculos[1] = (byte)eVehiculo.eVehP1;

                            GetVehiculosLibres(ref vVehiculos, 2, ref eVehIni, ref eVehFin);

                            AsignarNumeroVehiculo(eVehiculo.eVehP1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP2);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if ((eVehIni > 0 && eVehFin > 0) && ((int)eVehIni <= (int)eVehFin))
                            {
                                for (iIndex = (byte)eVehFin; iIndex >= (int)eVehIni; iIndex--)
                                {
                                    if (iIndex > (byte)eVehiculo.eVehC0)
                                        break;
                                    oInfoTagLeido.AgregarVehiculo(GetVehiculo(iIndex).NumeroVehiculo);
                                }
                            }
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP2).NumeroVehiculo;
                            break;

                        case '3':   //Asigno P1, P2 y P3
                            _logger.Info("ActivarAntena -> Asignar P1, P2 y P3");
                            vVehiculos[0] = (byte)eVehiculo.eVehP3;
                            vVehiculos[1] = (byte)eVehiculo.eVehP2;
                            vVehiculos[2] = (byte)eVehiculo.eVehP1;

                            GetVehiculosLibres(ref vVehiculos, 3, ref eVehIni, ref eVehFin);

                            AsignarNumeroVehiculo(eVehiculo.eVehP1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP2);
                            AsignarNumeroVehiculo(eVehiculo.eVehP3);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if ((eVehIni > 0 && eVehFin > 0) && ((int)eVehIni <= (int)eVehFin))
                            {
                                for (iIndex = (byte)eVehFin; iIndex >= (int)eVehIni; iIndex--)
                                {
                                    if (iIndex > (byte)eVehiculo.eVehC0)
                                        break;
                                    oInfoTagLeido.AgregarVehiculo(GetVehiculo(iIndex).NumeroVehiculo);
                                }
                            }
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP3).NumeroVehiculo;
                            break;

                        case 'C':   //Asigno P1 y C1
                            _logger.Info("ActivarAntena -> Asignar P1 y C1");
                            //Si C1 está vacio genero un evento de falla crítica
                            if (!GetVehiculo(eVehiculo.eVehC1).Ocupado)
                            {
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    if (!FallaSensoresDAC())
                                        GrabarLogSensores("ActivarAntena -> Asignar P1 y C1", eLogSensores.Logica_Sensores);
                            }

                            vVehiculos[0] = (byte)eVehiculo.eVehP1;
                            vVehiculos[1] = (byte)eVehiculo.eVehC1;

                            GetVehiculosLibres(ref vVehiculos, 2, ref eVehIni, ref eVehFin);

                            // Si P1 no esta vacío y C1 si, debe dejarse el no vacío siempre adelante
                            if ((GetVehiculo(eVehiculo.eVehP1).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP1).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehP1).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP1 = GetVehiculo(eVehiculo.eVehP1);
                                Vehiculo vehC1 = GetVehiculo(eVehiculo.eVehC1);

                                vehAux.CopiarVehiculo(ref vehP1);
                                vehP1.CopiarVehiculo(ref vehC1);
                                vehC1.CopiarVehiculo(ref vehAux);
                            }

                            AsignarNumeroVehiculo(eVehiculo.eVehC1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP1);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if ((eVehIni > 0 && eVehFin > 0) && ((int)eVehIni <= (int)eVehFin))
                            {
                                for (iIndex = (byte)eVehFin; iIndex >= (int)eVehIni; iIndex--)
                                {
                                    if (iIndex > (byte)eVehiculo.eVehC0)
                                        break;
                                    oInfoTagLeido.AgregarVehiculo(GetVehiculo(iIndex).NumeroVehiculo);
                                }
                            }
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo;
                            break;

                        case 'T':   //Asigno C1, P1 y P2
                            _logger.Info("ActivarAntena -> Asignar C1, P1 y P2");
                            //Si C1 está vacio genero un evento de falla crítica
                            if (!GetVehiculo(eVehiculo.eVehC1).Ocupado)
                            {
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    if (!FallaSensoresDAC())
                                        GrabarLogSensores("ActivarAntena -> Asignar C1, P1 y P2", eLogSensores.Logica_Sensores);
                            }

                            vVehiculos[0] = (byte)eVehiculo.eVehP2;
                            vVehiculos[1] = (byte)eVehiculo.eVehP1;
                            vVehiculos[2] = (byte)eVehiculo.eVehC1;

                            GetVehiculosLibres(ref vVehiculos, 3, ref eVehIni, ref eVehFin);

                            // Si P1 no esta vacío y C1 si, debe dejarse el no vacío siempre adelante
                            if ((GetVehiculo(eVehiculo.eVehP1).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP1).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehP1).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP1 = GetVehiculo(eVehiculo.eVehP1);
                                Vehiculo vehC1 = GetVehiculo(eVehiculo.eVehC1);

                                vehAux.CopiarVehiculo(ref vehP1);
                                vehP1.CopiarVehiculo(ref vehC1);
                                vehC1.CopiarVehiculo(ref vehAux);
                            }

                            // Si P2 no esta vacío lo movemos a P1
                            if ((GetVehiculo(eVehiculo.eVehP2).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP2).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehP1).NoVacio && !GetVehiculo(eVehiculo.eVehP2).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP2 = GetVehiculo(eVehiculo.eVehP2);
                                Vehiculo vehP1 = GetVehiculo(eVehiculo.eVehP1);

                                vehAux.CopiarVehiculo(ref vehP2);
                                vehP2.CopiarVehiculo(ref vehP1);
                                vehP1.CopiarVehiculo(ref vehAux);
                            }

                            AsignarNumeroVehiculo(eVehiculo.eVehC1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP2);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if ((eVehIni > 0 && eVehFin > 0) && ((int)eVehIni <= (int)eVehFin))
                            {
                                for (iIndex = (byte)eVehFin; iIndex >= (int)eVehIni; iIndex--)
                                {
                                    if (iIndex > (byte)eVehiculo.eVehC0)
                                        break;
                                    oInfoTagLeido.AgregarVehiculo(GetVehiculo(iIndex).NumeroVehiculo);
                                }
                            }
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP2).NumeroVehiculo;
                            break;

                        case '4':   //Asigno C1, P1, P2 y P3
                            _logger.Info("ActivarAntena -> Asignar C1, P1, P2 y P3");
                            //Si C1 está vacio genero un evento de falla crítica
                            if (!GetVehiculo(eVehiculo.eVehC1).Ocupado)
                            {
                                if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
                                    if (!FallaSensoresDAC())
                                        GrabarLogSensores("ActivarAntena -> Asignar C1, P1, P2 y P3", eLogSensores.Logica_Sensores);
                            }

                            vVehiculos[0] = (byte)eVehiculo.eVehP3;
                            vVehiculos[1] = (byte)eVehiculo.eVehP2;
                            vVehiculos[2] = (byte)eVehiculo.eVehP1;
                            vVehiculos[3] = (byte)eVehiculo.eVehC1;

                            GetVehiculosLibres(ref vVehiculos, 4, ref eVehIni, ref eVehFin);

                            // Si P1 no esta vacío y C1 si, debe dejarse el no vacío siempre adelante
                            if ((GetVehiculo(eVehiculo.eVehP1).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP1).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehP1).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP1 = GetVehiculo(eVehiculo.eVehP1);
                                Vehiculo vehC1 = GetVehiculo(eVehiculo.eVehC1);

                                vehAux.CopiarVehiculo(ref vehP1);
                                vehP1.CopiarVehiculo(ref vehC1);
                                vehC1.CopiarVehiculo(ref vehAux);
                            }

                            // Si P2 no esta vacío lo movemos a P1
                            if ((GetVehiculo(eVehiculo.eVehP2).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP2).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehP1).NoVacio && !GetVehiculo(eVehiculo.eVehP2).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP2 = GetVehiculo(eVehiculo.eVehP2);
                                Vehiculo vehP1 = GetVehiculo(eVehiculo.eVehP1);

                                vehAux.CopiarVehiculo(ref vehP2);
                                vehP2.CopiarVehiculo(ref vehP1);
                                vehP1.CopiarVehiculo(ref vehAux);
                            }

                            // Si P3 no esta vacío lo movemos a P2
                            if ((GetVehiculo(eVehiculo.eVehP3).EstaPagadoManual || GetVehiculo(eVehiculo.eVehP3).NoVacio) &&
                                !GetVehiculo(eVehiculo.eVehP2).NoVacio && !GetVehiculo(eVehiculo.eVehP3).EventoVehiculo)
                            {
                                Vehiculo vehAux = new Vehiculo();
                                Vehiculo vehP3 = GetVehiculo(eVehiculo.eVehP3);
                                Vehiculo vehP2 = GetVehiculo(eVehiculo.eVehP2);

                                vehAux.CopiarVehiculo(ref vehP3);
                                vehP3.CopiarVehiculo(ref vehP2);
                                vehP2.CopiarVehiculo(ref vehAux);
                            }

                            AsignarNumeroVehiculo(eVehiculo.eVehC1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP1);
                            AsignarNumeroVehiculo(eVehiculo.eVehP2);
                            AsignarNumeroVehiculo(eVehiculo.eVehP3);

                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if ((eVehIni > 0 && eVehFin > 0) && ((int)eVehIni <= (int)eVehFin))
                            {
                                for (iIndex = (byte)eVehFin; iIndex >= (int)eVehIni; iIndex--)
                                {
                                    if (iIndex > (byte)eVehiculo.eVehC0)
                                        break;
                                    oInfoTagLeido.AgregarVehiculo(GetVehiculo(iIndex).NumeroVehiculo);
                                }
                            }
                            _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP3).NumeroVehiculo;
                            break;
                        case 'F':   //Falla, no asigno ningun vehiculo
                            //Limpiar la lista de vehiculos
                            oInfoTagLeido.BorrarListaVehiculos();
                            if (bYaTenemosTag)
                            {
                                _logger.Info("ActivarAntena -> Falla, asignamos ultimo Tag leido a P1");
                                oInfoTagLeido.AgregarVehiculo(GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo);
                                _ultVehiculoAntena = GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo;
                            }
                            else
                                _logger.Info("ActivarAntena -> Falla, no asignamos ningun vehiculo");
                            break;
                        default:
                            bActAntena = true;  //Activo la antena siempre
                            break;
                    }
                }
                else
                {
                    bActAntena = true;  //Activo la antena siempre
                }

                _infoTagLeido = oInfoTagLeido;

                //Si activé la antena asigno el objeto a los datos del tag a leer
                if (bActAntena)
                {
                    if (!bReactivar)
                    {
                        //NO es mas necesario porque use un puntero
                        //m_InfoTagLeido = oInfoTagLeido;
                    }
                    //Actualizo m_nVehiculoIng pero no categorizo ni actualizo el dpy 
                    SetVehiculoIng(true);
                    //Reviso si hay algun tag listo para asignar
                    RevisarTagsleidos();
                }

            }

            _logger.Info("Número de Vehiculo[{Name}]", GetVehiculo(eVehiculo.eVehP1).NumeroVehiculo);
            //_logger.Info("ActivarAntena->bActAntena[{Name}] bYaTenemosTag[%c] m_bSinAntenaINI[%c] m_bInitAntenaOK[%c]", bActAntena ? 'S' : 'N', bYaTenemosTag ? 'S' : 'N', _bSinAntenaINI ? 'S' : 'N', _bInitAntenaOK ? 'S' : 'N');
            _logger.Info("ActivarAntena->bActAntena[{Name}] bYaTenemosTag[{Name}]", bActAntena ? 'S' : 'N', bYaTenemosTag ? 'S' : 'N');

            //Si está en modo quiebre liberado no activo la antena
            //Si ya tenemos tag no activamos
            if (bActAntena /*&& !bYaTenemosTag /*&& !_bSinAntenaINI*/ && _bInitAntenaOK &&
                !(_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera && _logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado))
            {
                _logger.Info("ActivarAntena->IN");

                //Seteo la causa de activacion (para saber si volvi a categorizar y aceptar la repeticion de la categoria)
                _causaLecturaTag = eCausaLecturaTag;

                bActAntena = true;

                //Si es por BPR o SIP BPR  recordamos el tiempo
                // ocupar BPA NO sino cuando sale el anterior me lee el tag de un tercero para un segundo
                if (eCausaLecturaTag == eCausaLecturaTag.eCausaSIPBPR || eCausaLecturaTag == eCausaLecturaTag.eCausaLazoPresencia)
                {
                    //Si habia un tag pendiente lo intentamos asignar
                    if (_infoTagEnCola.NumeroTag != "")
                    {
                        //Si VehIng ya fue desactivado, no le puedo asinar el tag
                        if (GetVehIngCat().PuedeAsignarTag(_tiempoLecturaTagEnCola))
                        {
                            if (DateTime.Now - _tiempoLecturaTagEnCola < _timeoutEnCola)
                            {
                                bActAntena = false;
                                AsignarTagEnCola(true);
                            }
                            else
                            {
                                _infoTagEnCola.Clear();
                                SetMismoTagIpico();
                            }
                        }
                        else
                        {
                            //Dejamos el tag en cola
                        }
                    }
                    else
                    {
                        GetVehIng().TiempoActivarAntena = DateTime.Now;
                    }
                }
                else
                {
                    _logger.Info("ActivarAntena -> marco para no activar Antena");
                }

                _tiempoDesactivacionAntena = DateTime.MinValue;

                if (bActAntena)
                {
                    ModuloAntena.Instance.ActivarAntena();
                }
                else
                {
                    _logger.Info("ActivarAntena -> No activo Antena ");
                }

                //Activo el timer de timeout de la antena
                _timerTimeoutAntena.Start();
            }
            _logger.Debug("ActivarAntena -> Fin ");
        }

        /// <summary>
        /// Revisa si alguno de los tags leidos se puede asignar o descartar
        /// </summary>
        private void RevisarTagsleidos()
        {
            InfoTagLeido oInfoTagLeido = null;
            Vehiculo oVehic = null;
            string sAux;
            bool bQuitado = false, bHayVehiculo = false;
            int iVehic = 0, UltVeh = 0, VehAct = 0;
            byte bVehicCola = 0;

            lock (_lockModificarListaTag)
            {
                try
                {
                    if (_lstInfoTagLeido.Count > 0)
                    {
                        _logger.Trace("RevisarTagsLeidos -> Inicio. Cantidad:[{Name}]", _lstInfoTagLeido.Count);
                        UltVeh = _lstInfoTagLeido.Count - 1;
                    }
                    //Recorro la lista de tags leidos y saco el vehículo más viejo
                    int pos = 0;
                    while (pos < _lstInfoTagLeido.Count)
                    {
                        bQuitado = false;
                        oInfoTagLeido = _lstInfoTagLeido[pos];
                        if (oInfoTagLeido.GetInfoTag().GetInit())
                        {
                            //Revisamos si se puede quitar algun vehiculo por no estar más o 
                            //ya estar Pagado
                            bHayVehiculo = false;
                            if (oInfoTagLeido.GetCantVehiculos() > 0)
                            {
                                for (iVehic = 0; iVehic < oInfoTagLeido.GetCantVehiculos(); iVehic++)
                                {
                                    for (bVehicCola = (int)eVehiculo.eVehP3; bVehicCola <= (int)eVehiculo.eVehC0; bVehicCola++)
                                    {
                                        oVehic = GetVehiculo(bVehicCola);
                                        if (oVehic.NumeroVehiculo == oInfoTagLeido.GetNumeroVehiculo(iVehic))
                                            if (oVehic.EstaImpago)
                                                bHayVehiculo = true;
                                            else
                                            {
                                                //Ya no esta o lo pagaron
                                                //Quitamos este vehiculo
                                                oInfoTagLeido.BorrarVehiculo(iVehic);
                                            }
                                    }
                                }
                            }

                            //Si la cantidad de vehiculos es 1 y hay un tag leído llamo a AsignarTagAVehiculo
                            if (oInfoTagLeido.GetCantVehiculos() == 1)
                            {
                                TimeSpan ctsDiff;
                                ctsDiff = DateTime.Now - oInfoTagLeido.GetInfoTag().FechaPago;
                                if (ctsDiff.TotalMilliseconds >= _timeoutTagsinVeh)
                                {
                                    //La lectura es vieja, la descarto
                                    //Quito el elemento de la lista que tiene como número de tag el de oInfoTag
                                    //Generando un transito cerrado
                                    if (LimpiarTagLeido(oInfoTagLeido.GetInfoTag()))
                                    {
                                        _logger.Info("RevisarTagsLeidos -> Es viejo generamos MA1 [{Name}]", oInfoTagLeido.GetInfoTag().FechaPago);
                                        //Comienzo nuevamente desde el principio porque AsignarTagAVehiculo
                                        //sacó el elemento de la lista
                                        pos = 0;
                                        bQuitado = true;
                                    }
                                }
                                else
                                {
                                    if (AsignarTagAVehiculo(oInfoTagLeido.GetInfoTag(), oInfoTagLeido.GetNumeroVehiculo(0)))
                                    {
                                        //Comienzo nuevamente desde el principio porque AsignarTagAVehiculo
                                        //sacó el elemento de la lista
                                        pos++;
                                        bQuitado = true;
                                    }
                                }
                            }
                            //Si no queda ningun vehiculo me fijo si el tag es viejo para sacarlo de la lista
                            //JMR y no es el ultimo de la cola 
                            else if (oInfoTagLeido.GetCantVehiculos() == 0
                                /*&& ((VehAct < UltVeh) || oInfoTagLeido.GetInfoTag().TagOK)*/) // Si no esta habilitado lo borramos para que no quede indefinidamente ocupando la cola de tags.
                            {
                                TimeSpan ctsDiff;
                                ctsDiff = DateTime.Now - oInfoTagLeido.GetInfoTag().FechaPago;
                                if (ctsDiff.TotalMilliseconds >= _timeoutTagsinVeh)
                                {
                                    //La lectura es vieja, la descarto
                                    //Quito el elemento de la lista que tiene como número de tag el de oInfoTag
                                    //Generando un transito cerrado
                                    if (LimpiarTagLeido(oInfoTagLeido.GetInfoTag()))
                                    {
                                        _logger.Info("RevisarTagsLeidos -> Es viejo generamos MA2 {0}", oInfoTagLeido.GetInfoTag().FechaPago);
                                        //Comienzo nuevamente desde el principio porque AsignarTagAVehiculo
                                        //sacó el elemento de la lista
                                        pos = 0;
                                        bQuitado = true;
                                    }
                                }
                            }
                        }

                        if (!bQuitado)
                        {
                            //Avanzo al siguiente elemento
                            pos++;
                        }
                        VehAct++;
                    }

                    //Revisamos tambien el tag que estamos leyendo
                    //Revisamos si se puede quitar algun vehiculo por no estar más o 
                    //ya estar Pagado
                    bHayVehiculo = false;
                    if (_infoTagLeido.GetCantVehiculos() > 0)
                    {
                        for (iVehic = 0; iVehic < _infoTagLeido.GetCantVehiculos(); iVehic++)
                        {
                            for (bVehicCola = (int)eVehiculo.eVehP3; bVehicCola <= (int)eVehiculo.eVehC0; bVehicCola++)
                            {
                                oVehic = GetVehiculo(bVehicCola);
                                if (oVehic.NumeroVehiculo == _infoTagLeido.GetNumeroVehiculo(iVehic))
                                    if (oVehic.EstaImpago)
                                        bHayVehiculo = true;
                                    else
                                    {
                                        //Ya no esta o lo pagaron
                                        //Quitamos este vehiculo
                                        _infoTagLeido.BorrarVehiculo(iVehic);
                                    }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _loggerExcepciones?.Error(e);
                }

            }
        }

        private void DesactivarAntenaTimer(bool bPorSiguiente)
        {
            _logger.Debug("DesactivarAntenaTimer -> Inicio");

            TimeSpan tiempo;

            if (bPorSiguiente)
                tiempo = _tiempoDesactAntDBPR; //modelo "D" por BPR, 1000mS por defecto
            else
                tiempo = _tiempoDesactAntD; //modelo "D" por liberar Separador, 100mS por defecto

            _tiempoDesactivacionAntena = DateTime.Now + tiempo;

            _timerDesactivarAntena.Interval = tiempo.TotalMilliseconds + 10;
            _timerDesactivarAntena.Start();

            _logger.Debug("DesactivarAntenaTimer -> Fin Interval[{name}]", _timerDesactivarAntena.Interval);
        }

        private void AsignarTagEnCola(bool bDarPagado)
        {
            InfoTag oInfoTagEnCola;
            string sDebug = "";

            _logger.Info("AsignarTagEnCola [{Name}] [{Name}] [{Name}]", bDarPagado ? "S" : "N", _infoTagEnCola.NumeroTag, GetVehIngCat().BloqueoTags ? "Bloqueado" : "");

            if (!GetVehIngCat().BloqueoTags)
            {
                AsignarTag(_infoTagEnCola, 0, eVehiculo.eVehIng);

                oInfoTagEnCola = _infoTagEnCola;
                _infoTagEnCola.Clear();
                _tiempoLecturaTagEnCola = DateTime.Now;

                //Verifico si el tag está habilitado
                if (oInfoTagEnCola.TagOK)
                {
                    //Si no está pagado asignamos el tag
                    if (_logicaCobro.Estado != eEstadoVia.EVAbiertaPag && TicketEnProceso == false && bDarPagado == true)
                        //Si lo validó OK -> Realizo el pago con TAG
                        TagIpicoVerificarManual();
                    else
                    {
                        //Poner Semaforo en Verde
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

                    }
                }
                else
                {
                    //Si no está pagado mostramos la categoria
                    if (_logicaCobro.Estado != eEstadoVia.EVAbiertaPag && !TicketEnProceso && bDarPagado)
                    {
                        //Si estaba libre la cambiamos a Categorizada
                        if (_logicaCobro.Estado == eEstadoVia.EVAbiertaLibre)
                            _logicaCobro.Estado = eEstadoVia.EVAbiertaCat;
                    }
                }
            }
            _logger.Debug("AsignarTagEnCola -> Salgo ");
        }
        /// <summary>
        /// Permito que la antena lea de nuevo el mismo tag
        /// </summary>
        private void SetMismoTagIpico(string sNroTag = "")
        {
            string sTag = string.IsNullOrEmpty(sNroTag) ? _ultTagValidado : sNroTag;
            //Limpiamos para poder leer de nuevo el tag 
            //si limpiamos mientras se está validando otro tag, no borrar estas variables
            _tagEnProceso = _tagEnProceso == sTag ? "" : _tagEnProceso;
            _ultTagValidado = _ultTagValidado == sTag ? "" : _ultTagValidado;
            _ultTagLeido = _ultTagLeido == sTag ? "" : _ultTagLeido;

            _ultTagValidadoTiempo = DateTime.Now;
            _ultTagLeidoTiempo = DateTime.Now;

            try
            {
                //borrar el tag de la lista de Tags leidos si lo encuentra
                var tag = _lstTagLeidoAntena.FirstOrDefault(f => f.NumeroTag.Contains(sTag));
                if (tag != null)
                    _lstTagLeidoAntena.Remove(tag);
            }
            catch (Exception e)
            {
                _loggerExcepciones.Error(e);
            }
        }

        private DateTime _UltimoLogSensores = DateTime.MinValue;
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
                TimeSpan intervalo = new TimeSpan(0, 5, 0);
                string sAux;
                LogLevel level;
                bool loguearSegunNivel = false;
                if (tipo == eLogSensores.Logica_Sensores)
                {
                    //Error de Logica
                    sAux = "Error de Logica";
                    level = LogLevel.Warn;
                    ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, new FallaCritica() { CodFallaCritica = EnmFallaCritica.FCPicLogica, Observacion = causa }, null);
                }
                else if (tipo == eLogSensores.Evento_Sensores)
                {
                    sAux = "Evento";
                    level = LogLevel.Debug;
                }
                else
                {
                    //Falla de Sensores
                    sAux = "Falla de Sensores";
                    level = LogLevel.Debug;
                }

                foreach (var rule in LogManager.Configuration.LoggingRules)
                {
                    if (rule.LoggerNamePattern == _loggerSensores.Name && rule.Levels.Contains(level))
                    {
                        loguearSegunNivel = true;
                        break;
                    }
                }

                if (loguearSegunNivel && (DateTime.Now > _UltimoLogSensores + intervalo))
                {
                    _loggerSensores.Log(level, "{Name} - Causa[{Name}] - Log[{Name}]", sAux, causa, DAC_PlacaIO.Instance.GetLogSensores());
                    _UltimoLogSensores = DateTime.Now;
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "GrabarLogSensores Causa[{Name}] Tipo[{Name}]", causa, tipo);
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
                //Reviso si en el vehiculo quedaron fotos y videos anteriores al ingreso del vehiculo
                TimeSpan intervalo;
                if (GetVehiculo(eVehiculo).ListaInfoVideo.Any(x => x != null))
                {
                    intervalo = new TimeSpan(0, 0, _tiempoDescarteVideosReversaSeg);
                    GetVehiculo(eVehiculo).ListaInfoVideo.RemoveAll(x => x != null && x.FechaAgregado < DateTime.Now - intervalo);
                }
                if (GetVehiculo(eVehiculo).ListaInfoFoto.Any(x => x != null))
                {
                    intervalo = new TimeSpan(0, 0, _tiempoDescarteFotosReversaSeg);
                    GetVehiculo(eVehiculo).ListaInfoFoto.RemoveAll(x => x != null && x.FechaAgregado < DateTime.Now - intervalo);
                }

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
            return ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroVehiculo);
        }


        public override Vehiculo GetVehiculo(eVehiculo eVehiculo)
        {
            //return m_vVehiculo[(int)eVehiculo];	
            return GetVehiculo((byte)eVehiculo);
        }

        public Vehiculo GetVehiculo(byte btIndex)
        {
            if (btIndex >= 0 && btIndex < _vVehiculo.Length)
            {
                return _vVehiculo[btIndex];
            }
            else
            {
                return new Vehiculo();
            }
        }

        private void GetVehiculosLibres(ref byte[] btvVeh, byte btCant,
                                  ref eVehiculo eVehIni, ref eVehiculo eVehFin)
        {
            byte btVehIni = 0, btVehFin = 0;
            int i = 0;
            bool bFin = false;
            while (!bFin && i < btCant)
            {
                //Si el tag leido tuvo problemas de escritura lo mantenemos en la lista
                if (!(GetVehiculo(btvVeh[i]).NoVacio &&
                    ((GetVehiculo(btvVeh[i]).InfoTag.Init && GetVehiculo(btvVeh[i]).InfoTag.TagOK)
                    || GetVehiculo(btvVeh[i]).FormaPago > 0) &&
                    !bFin))
                {
                    if (btVehIni == 0)
                        btVehIni = btvVeh[i];
                    btVehFin = btvVeh[i];
                }
                else
                {
                    bFin = true;
                }
                i++;
            }

            eVehIni = (eVehiculo)btVehIni;
            eVehFin = (eVehiculo)btVehFin;
            _logger.Debug("GetVehiculosLibres -> eVehIni [{0}] eVehFin [{1}]", eVehIni, eVehFin);
        }

        private void TagIpicoVerificarManual()
        {
            InfoTag oTag = null;
            eErrorTag lastErrorTag = eErrorTag.NoError;
            Vehiculo VehIng = null;

            try
            {
                if (_procesaTagIpico == false)
                {
                    _procesaTagIpico = true;
                    VehIng = GetVehIngCat();

                    _logger.Info("TagIpicoVerificarManual->Inicio Vehiculo [{Name}] [{Name}] [{Name}] [{Name}]", VehIng.NumeroVehiculo, VehIng.InfoTag.TagOK ? 'S' : 'N', VehIng.InfoTag.ErrorTag, VehIng.InfoTag.TipOp);

                    lastErrorTag = VehIng.InfoTag.ErrorTag;

                    oTag = VehIng.InfoTag;
                    if (oTag.GetTagHabilitado())
                    {
                        //En Modelo D sumamos al turno y chequeamos la barrera y semaforo
                        if (VehIng.InfoTag.TipBo == 'U')
                        {
                            //Reseteo el flag de categorización					
                            if (VehIng.Categoria == 0 || VehIng.Categoria == VehIng.InfoTag.Categoria)
                            {
                                _ucCatego_IngTag = 0;
                                _sCatego_Confirmada = 0;
                                VehIng.TransitoUFRE = true;
                                _procesaTagIpico = false;
                                return;
                            }
                            else
                            {
                                VehIng.TransitoUFRE = false;
                                _procesaTagIpico = false;
                                return;
                            }

                        }
                        else if (VehIng.InfoTag.TipBo == 'F')
                        {
                            //Reseteo el flag de categorización
                            _ucCatego_IngTag = 0;
                            _sCatego_Confirmada = 0;

                            VehIng.Federado = true;
                            _procesaTagIpico = false;
                            return;
                        }
                        else if (VehIng.InfoTag.TipOp == 'C')
                        {
                            _procesaTagIpico = false;
                            return;
                        }

                        RevisarBarreraTagManual();
                        //Sumar al turno
                        RegistroPagoTag(ref oTag, ref VehIng);

                        //Capturo foto y video
                        DecideCaptura(eCausaVideo.PagadoTelepeaje, VehIng.NumeroVehiculo);

                        //Si el modelo es D  ejecuto SetVehiculoIng
                        //para actualizar el estado
                        SetVehiculoIng();
                    }
                    else
                    {
                        bool bEnviar = false;

                        //Enviamos el evento si cambia el codigo
                        // y el tag no está OK
                        if (lastErrorTag != oTag.ErrorTag
                            && !oTag.TagOK)
                            bEnviar = true;
                    }
                    _procesaTagIpico = false;
                }
                else
                    _logger.Debug("TagIpicoVerificarManual -> Ya está en proceso");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "Error TagIpicoVerificarManual");
            }
        }

        public override bool FallaSensoresDAC(int btFiltrarSensores = 0)
        {
            bool bRetCode = false;
            //Si no está la vía en sentido opuesto
            if (!_ultimoSentidoEsOpuesto && !IsSentidoOpuesto(true))
            {
                byte PICErr = 0, bEstado = 0, bValor = 0, bSensor = 0;
                byte PICErrReal = 0;
                string sMsg = "", sMsg2 = "", strEstado = "", strComentario = "";
                short ret = 0;

                ret = DAC_PlacaIO.Instance.ObtenerFallaSensores(ref PICErr, ref bEstado);

                if (ret < 0)
                {
                    if (_logicaCobro?.Modo?.Modo != "D")
                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.ErrorComunicacionDAC, 0);
                    else
                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.ErrorComunicacionDACSinCajero, 0);

                    // No me pude comunicar
                    ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, new FallaCritica() { CodFallaCritica = EnmFallaCritica.FCPic, Observacion = DAC_PlacaIO.Instance.ObtenerPicError(ret) }, null);
                    _logger.Info("FallaSensoresDAC-> Error de comunicacion con DAC - ret[{0}]", ret);
                }
                else
                {
                    if (_logicaCobro?.Modo?.Modo != "D")
                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.ErrorComunicacionDAC, 0);
                    else
                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.ErrorComunicacionDACSinCajero, 0);

                    //Filtramos los sensores
                    PICErr = (byte)((PICErr & (~btFiltrarSensores)) & 0xff);
                    if (PICErr > 0)
                    {
                        strEstado = ((EstDinamica)bEstado).ToString();
                        DAC_PlacaIO.Instance.TraducirFallaSensoresDin(PICErr, bEstado, ref PICErrReal, ref strComentario, ref bValor);

                        bRetCode = true;
                        sMsg = GetMSGPICError(PICErr, false, ref bSensor) + ", Estado de falla: " + strEstado;

                        sMsg = "Sensor Inesperado: " + sMsg;
                        //Actualizamos el status para el Online
                        sMsg2 = GetMSGPICError(PICErrReal, true, ref bSensor);
                        _logger.Info($"FallaSensoresDAC -> [{sMsg2}] Valor: [{bValor}] [{strComentario}]");
                        if (bSensor > 0)
                        {
                            IncContAlarmas(PICErrReal);

                            _logger.Info("Error en Sensores [{Name}]", strComentario);

                            EventoError oEventoError = new EventoError();
                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                            oEventoError.Sensor = (eSensorEvento)bSensor;
                            oEventoError.Valor = bValor;
                            oEventoError.Observacion = sMsg;

                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, null);

                            if ((eSensorEvento)bSensor == eSensorEvento.BPR)
                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FallaBPR, 0);
                            else
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FallaBPR, 0);
                        }
                        else
                            ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FallaBPR, 0);

                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, new FallaCritica() { CodFallaCritica = EnmFallaCritica.FCPic, Observacion = sMsg }, null);

                        _logger.Info("FallaSensoresDAC-> [{Name}] Valor: [{Name}] [{Name}]", sMsg, bValor, strComentario);
                        GrabarLogSensores("FallaSensoresDAC", eLogSensores.FallaSensores);
                    }
                }
            }

            return bRetCode;
        }

        private void IncContAlarmas(byte PICErr)
        {
            if (((PICErr >> 0) & 0x01) > 0)
            {
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailLazo, 0);
            }
            else
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailLazo, 0); //Al parece no se puede poner aca.
            }

            //BPR es el lazo de entrada de la via AVI
            if (((PICErr >> 1) & 0x01) > 0)
            {
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailLazo, 1);
            }
            else
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailLazo, 1);
            }

            //BPI es el lazo de salida de la via AVI
            if (((PICErr >> 2) & 0x01) > 0)
            {
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailLazo, 2);
            }
            else
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailLazo, 2);
            }

            if (((PICErr >> 3) & 0x01) > 0)
            {
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailLazo, 3);
            }
            else
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailLazo, 3);
            }

            //BPA es lazo de salida de la via manual
            if (((PICErr >> 4) & 0x01) > 0)
            {
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailLazo, 4);
            }
            else
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailLazo, 4);
            }

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
                inCantErrores++;
                strRet += "Separador Entrada";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_SEPENT;
            }

            if (((PICErr >> 1) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Lazo de Presencia";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPR;
            }

            if (((PICErr >> 2) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Lazo Intermedio";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPI;
            }

            if (((PICErr >> 3) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Separador Vehicular Salida";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_SEPSAL;
            }

            if (((PICErr >> 4) & 0x01) > 0)
            {
                inCantErrores++;
                strRet += "Lazo Salida Principal";
                strRet += ", ";
                bErrorSensor = ERROR_SENSOR_BPA;
            }

            if (((PICErr >> 5) & 0x01) > 0)
            {
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
        /// Pausa el thread de AVI y desactiva la antena
        /// </summary>
        /// <param name="sCausa">Indica el motivo de desactivación de la antena</param>
        /// <returns></returns>
        public override void DesactivarAntena(eCausaDesactivacionAntena causa)
        {
            _logger.Info("DesactivarAntena -> Inicio");
            //Modelo D
            //Puede ser por desactivar separador o activar BPR
            //en este ultimo caso acaba de entrar a la via
            //lo puedo reconocer porque el vehiculo para el que active la antena 
            //esta todavia en la cola

            //Marcamos al menor de los vehiculos (el mas viejo)
            //como que ya entro a la via
            if (causa == eCausaDesactivacionAntena.Sensores)
            {
                ulong ulNroVehiculo = 0;
                bool bEnc = false;
                int i = 0;
                Vehiculo oVeh = null;

                ulNroVehiculo = _ultVehiculoAntena;

                i = (int)eVehiculo.eVehP3;
                while (!bEnc && i <= _vVehiculo.GetLength(0))
                {
                    oVeh = GetVehiculo((eVehiculo)i);
                    if (oVeh.NumeroVehiculo == ulNroVehiculo)
                    {
                        //oVeh.VehEntro = true;
                        bEnc = true;
                        _logger.Info("DesactivarAntena: Marcamos Veh como que entró a la vía Último Vehiculo[{Name}] NumeroVehiculo[{Name}] NumeroTag[{Name}]", ulNroVehiculo, oVeh.NumeroVehiculo, oVeh.InfoTag.NumeroTag);
                    }
                    i++;
                }
            }

            _timerTimeoutAntena.Stop();

            if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
            {
                ModuloAntena.Instance.DesactivarAntena();
            }

            //Solo lo hacemos si todavia no leimos un tag
            //Si lo estamos validando debe poder aceptarlo
            if (_tagEnProceso == "")
                _infoTagLeido.BorrarListaVehiculos();

            _logger.Info("DesactivarAntena -> Fin");

        }

        /// <summary>
        /// Genera un evento de transito o de violación con el vehículo eVehiculo
		/// si está ocupado y tiene forma de pago válida(transito)
        ///o no(violación). Si esta ocupado luego borra el vehículo
        /// </summary>
        /// <param name="eVehiculo">enumerado del vehículo a enviar</param>
        private void GenerarEventoSiOcupado(eVehiculo vehiculo)
        {
            Vehiculo oVehiculo;
            oVehiculo = GetVehiculo(vehiculo);
            GenerarEventoSiOcupado(ref oVehiculo);
            _vVehiculo[(int)vehiculo] = new Vehiculo();
        }

        /// <summary>
        /// Genera un evento de transito o de violación con el vehículo eVehiculo
        /// si está ocupado y tiene forma de pago válida(transito)
        /// o no(violación). Si esta ocupado luego borra el vehículo
        /// </summary>
        /// <param name="oVehiculo">vehículo a enviar</param>
        private void GenerarEventoSiOcupado(ref Vehiculo oVehiculo)
        {
            _logger.Info("GenerarEventoSiOcupado -> Inicio NroVeh:[{Name}]", oVehiculo.NumeroVehiculo);


            if (oVehiculo.NoVacio)
            {
                //Si esta trabajando en el Sentido Opuesto o trabajo por ultima vez en sentido opuesto
                //y Estaba Cerrada
                if (_logicaCobro.Estado == eEstadoVia.EVCerrada && (IsSentidoOpuesto(false) || _ultimoSentidoEsOpuesto))
                {
                    ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionAutista);
                    oVehiculo.Clear(false);
                }
                else
                {
                    _logger.Info("GenerarEventoSiOcupado -> NroVeh: [{Name}], genero transito, violacion o salida anomala", oVehiculo.NumeroVehiculo);
                    oVehiculo.EventoVehiculo = true; //Flag para no limpiar el vehiculo mientras se genera un evento

                    //Si no está pagado y el flag de error de lógica es true, generamos salida anomala
                    if (!oVehiculo.EstaPagado && oVehiculo.ErrorDeLogica && !DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
                    {
                        ModuloEventos.Instance.SetSalidaAnomala(_logicaCobro.GetTurno, oVehiculo, "AT");
                    }
                    else
                    {
                        //Si está pagado genero un evento de tránsito, sino de violación
                        if (oVehiculo.EstaPagado)
                        {
                            _estadoAbort = ' ';        //Transito Normal
                            RegistroTransito(ref oVehiculo);
                        }
                        else
                        {
                            //si están null realizar new list para no causar excepción por el .Any() y copiar las fotos/videos
                            oVehiculo.ListaInfoVideo = oVehiculo.ListaInfoVideo == null ? oVehiculo.ListaInfoVideo = new List<InfoMedios>() : oVehiculo.ListaInfoVideo;
                            oVehiculo.ListaInfoFoto = oVehiculo.ListaInfoFoto == null ? oVehiculo.ListaInfoFoto = new List<InfoMedios>() : oVehiculo.ListaInfoFoto;

                            if (oVehiculo?.ListaInfoVideo != null && _ultimoVideo != null)
                            {
                                if (!oVehiculo.ListaInfoVideo.Any(x => x != null) && _ultimoVideo.Any(x => x != null))
                                {
                                    oVehiculo.ListaInfoVideo = _ultimoVideo;
                                    _ultimoVideo.Clear();
                                }
                            }
                            if (oVehiculo?.ListaInfoFoto != null)
                            {
                                if (!oVehiculo.ListaInfoFoto.Any(x => x != null))
                                {
                                    eCausaVideo sensor = eCausaVideo.Violacion;
                                    CapturaFoto(ref oVehiculo, ref sensor);
                                }
                            }

                            Violacion(ref oVehiculo);
                        }

                        EnviarEventoVehiculo(ref oVehiculo);
                    }
                    oVehiculo.EventoVehiculo = false;
                }
            }
            _logger.Info("GenerarEventoSiOcupado -> Fin NroVeh:[{Name}]", oVehiculo.NumeroVehiculo);
        }


        /// <summary>
        /// Carga los datos en el vehículo pVehiculo de la operación abortada
        /// </summary>
        /// <param name="oVehiculo">vehículo donde se guardan los datos de la op. abortada</param>
        override public void OpAbortadaEvento(ref Vehiculo oVehiculo)
        {
            try
            {
                _logger.Info("OpAbortadaEvento->OpAbortada, Se ejecuta");

                //Si no tenia fecha
                if (oVehiculo.Fecha == DateTime.MinValue)
                    //Fecha del evento
                    oVehiculo.Fecha = DateTime.Now;//(m_Fecha);

                //Asigno el tipo de operación y el número de transito
                oVehiculo.Operacion = "AB";

                //Finalizo los videos que estaban grabando
                ModuloVideo.Instance.DetenerVideo(oVehiculo, eCausaVideo.Nada, eCamara.Lateral);
                int index = oVehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
                if (index != -1)
                    oVehiculo.ListaInfoVideo[index].EstaFilmando = false;

                if (oVehiculo.NumeroTransito == 0)
                    oVehiculo.NumeroTransito = IncrementoTransito();

                //Enviamos la ultima huella
                oVehiculo.Huella = _huellaDAC;
                //Limpio la ultima huella leida
                _huellaDAC = string.Empty;

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

                //Analizo Sin Tomar en cuenta el evento
                //" ":Solo Categoria
                DecideAlmacenar(eAlmacenaMedio.Categoria, ref oVehiculo);


                if (oVehiculo.CodigoObservacion > 0)
                    DecideAlmacenar(eAlmacenaMedio.Observado, ref oVehiculo);


                //Almacena transito abortado en categoria correspondiente
                ModuloBaseDatos.Instance.AlmacenarAbortadoTurno(oVehiculo, _logicaCobro.GetTurno);

                //Almacena transito con categoria 0
                ModuloBaseDatos.Instance.AlmacenarTransitoTurno(oVehiculo, _logicaCobro.GetTurno);

                //Incremento viol6,ABORx (si no es viaje reciente)
                ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Abort);

                //Almaceno la información del Tag cancelado solo si fue leido por antena
                if (oVehiculo.InfoTag.LecturaManual != 'S')
                    _ultTagCancelado = oVehiculo.InfoTag;
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            _logger.Info("OpAbortadaEvento->OpAbortada, finaliza");
        }

        /// <summary>
        /// Genera un evento de op. abortada para la vía en modo Dinámico
        /// </summary>
        override public void OpAbortadaModoD(bool bAutomatico = false, eVehiculo eVeh = eVehiculo.eVehP1, string nroTag = "")
        {
            ulong numeroVehiculo = 0;
            Vehiculo oVehIng = null;
            bool bOcup = false;

            if (!bAutomatico)
            {
                eVeh = GetVehiculoIng();
                oVehIng = _vVehiculo[(int)eVeh];
            }
            else
            {
                oVehIng = GetVehiculo(eVeh);

                // El vehiculo se movió, lo busco por numero de tag
                if (!string.IsNullOrEmpty(nroTag) && oVehIng.InfoTag.NumeroTag != nroTag)
                {
                    int i = (int)eVehiculo.eVehP3;
                    bool bEnc = false;
                    int total = (int)eVehiculo.eVehC0;
                    string nroTagLocal = "";

                    while (!bEnc && i <= total)
                    {
                        nroTagLocal = GetVehiculo((eVehiculo)i).InfoTag.NumeroTag;

                        if (!string.IsNullOrEmpty(nroTagLocal) && nroTagLocal == nroTag)
                        {
                            bEnc = true;
                        }
                        i++;
                    }
                    if (bEnc)
                    {
                        oVehIng = GetVehiculo((eVehiculo)(i - 1));
                        eVeh = (eVehiculo)(i - 1);
                    }
                }
            }

            _logger.Info("OpAbortadaModoD NumVeh[{Name}] Ocupado[{Name}] FormaPago[{Name}] eVeh[{Name}] Tag[{Name}]", oVehIng.NumeroVehiculo, oVehIng.Ocupado ? 'S' : 'N', oVehIng.FormaPago, bAutomatico ? eVeh.ToString() : "NO", nroTag);

            oVehIng.EventoVehiculo = true; //marcamos flag para que no se mueva el veh mientras lo sacamos

            //Eliminamos el tag de la lista de la antena para poder leerlo nuevamente
            if (!string.IsNullOrEmpty(oVehIng.InfoTag.NumeroTag))
            {
                ModuloAntena.Instance.BorrarTag(oVehIng.InfoTag.NumeroTag);
                SetMismoTagIpico(oVehIng.InfoTag.NumeroTag);
            }

            //Me fijo si el vehículo Ing tiene forma de pago
            //NO PERMITIMOS ABORTAR en el separador porque está demasiado cerca de la barrera
            if (bAutomatico || (!_enSeparSal && oVehIng.EstaPagado))
            {
                OpAbortadaEvento(ref oVehIng);

                //se copia al online
                Vehiculo vehOnline = new Vehiculo();
                vehOnline.CopiarVehiculo(ref oVehIng);
                _vVehiculo[(int)eVehiculo.eVehOnLine] = vehOnline;
                EnviarEventoVehiculo(ref oVehIng);
                oVehIng.EventoVehiculo = false;

                numeroVehiculo = oVehIng.NumeroVehiculo;
                bOcup = oVehIng.Ocupado;

                if (oVehIng.TipOp == 'E' && oVehIng.FallaTicketF != 'M')
                {
                    //m_pImprimeTicket->CancelTicket(VehIng->GetTarifa());
                }

                if (bAutomatico && (GetPrimerVehiculo()?.InfoTag.NumeroTag == oVehIng.InfoTag.NumeroTag))
                {
                    ModuloPantalla.Instance.LimpiarVehiculo(new Vehiculo());
                    ModuloPantalla.Instance.LimpiarMensajes();
                    // Se envia mensaje al display
                    ModuloDisplay.Instance.Enviar(eDisplay.BNV);
                }

                //antes de limpiar buscamos el vehiculo, por si se movió
                if (!bAutomatico)
                    eVeh = GetVehiculoIng();
                else
                {
                    // Si es VehTra, no busco ningun vehiculo
                    if (eVeh <= eVehiculo.eVehC0 && eVeh >= eVehiculo.eVehP3)
                        eVeh = BuscarVehiculo(0, false, true, oVehIng.InfoTag.NumeroTag);
                }

                //Limpio el vehículo Ing
                _vVehiculo[(int)eVeh] = new Vehiculo();
                oVehIng = _vVehiculo[(int)eVeh];

                //Pongo el flag de vehículo ocupado y el número del vehículo
                if (!bAutomatico)
                {
                    oVehIng.Ocupado = bOcup;
                    oVehIng.NumeroVehiculo = numeroVehiculo;
                    oVehIng.TieneTagCancelado = true;
                }

                //Si el lazo está libre bajo la barrera
                if (!_enLazoSal && !bAutomatico)
                {
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                    _logger.Info("OpAbortadaModoD -> BARRERA ABAJO!!");
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                }

                SetVehiculoIng();
                LoguearColaVehiculos();
            }
            else
                _logger.Info("OpAbortadaModoD -> NO PERMITIMOS ABORTAR en el separador porque está demasiado cerca de la barrera");

            _logger.Info("OpAbortadaModoD -> Fin");
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="origenTecla">indica si es una op cerrada por teclado o por reinicio. 1 -> op cerrada. 0 -> reinicio</param>
        /// <param name="oVehiculo"></param>
        override public void OpCerradaEvento(short origenTecla, ref Vehiculo oVehiculo)
        {
            _logger.Info($"OpCerradaEvento -> Inicio NroVeh[{oVehiculo.NumeroVehiculo}]");

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
                else if (origenTecla == 2)
                {
                    //Si no tenia fecha
                    if (oVehiculo.Fecha == DateTime.MinValue)
                    {
                        oVehiculo.Fecha = DateTime.Now;
                    }
                    FechaCerrada = oVehiculo.Fecha;
                    sOper = "AT";
                    bApagada = true;
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
                if (oVehiculo.NumeroTransito == 0)
                    oVehiculo.NumeroTransito = IncrementoTransito();

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
                //se copia al online
                Vehiculo vehOnline = new Vehiculo();
                vehOnline.CopiarVehiculo(ref oVehiculo);
                _vVehiculo[(int)eVehiculo.eVehOnLine] = vehOnline;
                EnviarEventoVehiculo(ref oVehiculo);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logger.Info("OpCerradaEvento -> FIN");
            LoguearColaVehiculos();
        }

        /// <summary>
        /// Carga el objeto pVehiculo con los datos necesarios para generar
        /// luego un evento de transito. Tambien registra el transito en los contadores
        /// </summary>
        /// <param name="oVehiculo">referencia al vehículo con el cual, se arma el evento de transito</param>
        private void RegistroTransito(ref Vehiculo oVehiculo)
        {
            _logger.Info($"RegistroTransito -> Inicio NroVeh[{oVehiculo.NumeroVehiculo}]");

            try
            {
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.Violaciones, 0);
                DateTime T1 = DateTime.Now;

                oVehiculo.Operacion = "TR";

                //Sino tiene número de transito le asigno uno
                /*if (oVehiculo.NumeroTransito == 0)
                {
                    oVehiculo.NumeroTransito = IncrementoTransito();
                }*/
                //Sino tiene fecha de transito le asigno una
                if (oVehiculo.Fecha == DateTime.MinValue)
                {
                    oVehiculo.Fecha = T1;
                }
                if (!oVehiculo.InfoDac.YaTieneDAC)
                {
                    CategoriaPic(ref oVehiculo, T1, true, null);
                    // Se envia mensaje a modulo de video continuo
                    ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.Detectado, null, null, oVehiculo);
                }

                _huellaDAC = string.Empty;

                if (oVehiculo.InfoDac.Categoria > 0)
                {
                    TarifaABuscar oTarifaABuscar = new TarifaABuscar();

                    oTarifaABuscar = GenerarTarifaABuscar(oVehiculo.InfoDac.Categoria, oVehiculo.TipoTarifa);

                    Tarifa oTarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar);

                    if (oTarifa != null)
                    {
                        oVehiculo.InfoDac.Tarifa = oTarifa.Valor;
                        oVehiculo.CategoriaDesc = oTarifa.Descripcion;
                    }

                    if (oVehiculo.InfoDac.Categoria != oVehiculo.Categoria)
                    {
                        if (oTarifa?.Valor > oVehiculo.Tarifa)
                        {
                            DecideAlmacenar(eAlmacenaMedio.DiscrepanciaEncontra, ref oVehiculo);

                            ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Disc);
                        }
                        else
                        {
                            DecideAlmacenar(eAlmacenaMedio.DiscrepanciaFavor, ref oVehiculo);

                            ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.DiscF);
                        }

                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.DiscrCatego, 0);
                    }
                    else
                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.DiscrCatego, 0);

                }
                else
                {
                    if (oVehiculo.InfoDac.Categoria == -1)
                    {
                        if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                        {
                            ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.FLazo);
                        }
                    }
                    else if (oVehiculo.InfoDac.Categoria == -2)
                    {
                        if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                        {
                            ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.FSensor);
                        }
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

                //actualizamos numero de tránsito
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                Vehiculo veh = new Vehiculo();
                veh.NumeroTransito = oVehiculo.NumeroTransito;
                ClassUtiles.InsertarDatoVia(veh, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                ModuloBaseDatos.Instance.AlmacenarTransitoTurno(oVehiculo, _logicaCobro.GetTurno);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
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

            _logger.Info("DecideAlmacenar -> Inicio NroVen[{0}] Causa[{1}]", oVehiculo.NumeroVehiculo, causa.ToString());

            try
            {
                // Chequeo si ya se almacenaron videos/fotos de este vehiculo
                bool FotoSinAlmacenar = false, VideoSinAlmacenar = false;
                foreach (InfoMedios infoM in oVehiculo.ListaInfoFoto)
                {
                    if (infoM != null && !infoM.Almacenar)
                        FotoSinAlmacenar = true;
                }
                foreach (InfoMedios infoM in oVehiculo.ListaInfoVideo)
                {
                    if (infoM != null && !infoM.Almacenar)
                        VideoSinAlmacenar = true;
                }

                //Reviso si en el vehiculo quedaron fotos y videos anteriores al ingreso del vehiculo
                TimeSpan intervalo = new TimeSpan(0, _tiempoDescarteImagenesAlmacenarMin, 0);
                if (oVehiculo.ListaInfoVideo.Any(x => x != null))
                    oVehiculo.ListaInfoVideo.RemoveAll(x => x != null && x.FechaAgregado < DateTime.Now - intervalo);
                if (oVehiculo.ListaInfoFoto.Any(x => x != null))
                    oVehiculo.ListaInfoFoto.RemoveAll(x => x != null && x.FechaAgregado < DateTime.Now - intervalo);

                // Si ya se almacenaron todos, no chequeo condiciones para almacenar
                if (!FotoSinAlmacenar && !VideoSinAlmacenar)
                {
                    _logger.Debug("DecideAlmacenar -> Almacena sin chequear condiciones");

                    ModuloVideo.Instance.AlmacenarVideo(ref oVehiculo);
                    ModuloFoto.Instance.AlmacenarFoto(ref oVehiculo);
                    return;
                }

                // Consulto por porcentaje de almacenamiento por tipo de evento
                List<VideoEve> listaVideoEve = null;
                if (causa != eAlmacenaMedio.Categoria)
                {
                    listaVideoEve = ModuloBaseDatos.Instance.BuscarVideoEve(causa.GetDescription());
                    if (listaVideoEve?.Count > 0)
                    {
                        foundEve = true;
                        almacena = listaVideoEve[0].Almacena;
                    }
                    else
                    {
                        _logger.Debug("DecideAlmacenar -> BuscarVideoEve: No se encontro la causa en la base de datos ");
                    }
                }

                if (listaVideoEve != null && foundEve) //Defino porcentaje de almacenamiento segun dato de la consulta
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

                int Categoria = !string.IsNullOrEmpty(oVehiculo.InfoTag?.NumeroTag) && oVehiculo.Categoria == 0 ? oVehiculo.InfoTag.Categoria : oVehiculo.Categoria;
                if (Categoria == 0) Categoria = oVehiculo.InfoDac.Categoria;  //Si por alguna razon todavia sigue en 0, asigno la categoria del DAC.
                List<VideoCat> listaVideoCat = ModuloBaseDatos.Instance.BuscarVideoCat(Categoria);
                if (listaVideoCat?.Count >= 1)
                {
                    foundCat = true;
                    almacena = listaVideoCat[0].Almacena;
                }
                else
                {
                    _logger.Debug("DecideAlmacenar -> No se encontro la categoria en la base de datos ");
                }

                if (listaVideoCat != null && foundCat) //Defino porcentaje de almacenamiento segun dato de la consulta
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

            _logger.Info("DecideAlmacenar -> Fin");
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

            DateTime T1 = DateTime.Now;
            bool ret = false;
            oVehiculo.ProcesandoViolacion = true; //flag para no procesar ninguna forma de cobro mientras se realiza la violación
            try
            {
                //Finaliza lectura de Tchip
                ModuloTarjetaChip.Instance.FinalizaLectura();

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
                CategoriaPic(ref oVehiculo, T1, true, null);//Recibe la categorizacion que hizo el PIC 

                //Recuerdo la huellac
                _huellaDAC = oVehiculo.Huella;

                //En modo MD Solo hay violacion si hay peanas y el vehiculo no salio chupado
                //O el modo permite la violacion sin peanas
                bool usaPeanas = false;
                if (_logicaCobro.ModoPermite(ePermisosModos.ViolacionSinPeanas))
                    usaPeanas = true;
                else
                    usaPeanas = oVehiculo.InfoDac.HayPeanas;

                if (usaPeanas && !oVehiculo.SalioChupado)
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
                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.Violaciones, 0);

                        if (_logicaCobro.Estado == eEstadoVia.EVQuiebreBarrera)
                        {
                            oVehiculo.ModoBarrera = 'Q';
                        }
                        else
                        {
                            if (ClassUtiles.IsNullChar(oVehiculo.ModoBarrera))
                                oVehiculo.ModoBarrera = ' ';
                        }

                        _logger.Debug("LogicaViaDinamica::Violacion -> ModoBarrera[{0}]", oVehiculo.ModoBarrera);


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

                            switch (_logicaCobro.Estado)
                            {
                                case eEstadoVia.EVCerrada:
                                    oVehiculo.TipoViolacion = 'R';
                                    ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesPrevias);
                                    break;

                                case eEstadoVia.EVQuiebreBarrera:
                                    //Incremento el total de violaciones por quiebre de barrera

                                    if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreControlado)
                                    {
                                        oVehiculo.TipoViolacion = 'U';
                                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesQuiebreBarrera);
                                    }
                                    else if (_logicaCobro.ModoQuiebre == eQuiebre.EVQuiebreLiberado)
                                    {
                                        oVehiculo.TipoViolacion = 'Q';
                                        ModuloBaseDatos.Instance.IncrementarContador(eContadores.ViolacionesQuiebreBarrera);
                                    }
                                    break;

                                default:
                                    oVehiculo.TipoViolacion = 'I';
                                    break;
                            }

                            //Si hay algun video filmando, lo detengo
                            ModuloVideo.Instance.DetenerVideo(oVehiculo, eCausaVideo.Nada, eCamara.Lateral);
                            int index = oVehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
                            if (index != -1)
                                oVehiculo.ListaInfoVideo[index].EstaFilmando = false;

                            //V:Violacion
                            DecideAlmacenar(eAlmacenaMedio.Violacion, ref oVehiculo);

                            //Incremento el número de transitos
                            if (oVehiculo.NumeroTransito == 0)
                                oVehiculo.NumeroTransito = IncrementoTransito();
                            //LoguearColaVehiculos();

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

                            // Se envia mensaje a modulo de video continuo
                            TarifaABuscar oTarifaABuscar = new TarifaABuscar();
                            oTarifaABuscar = GenerarTarifaABuscar(oVehiculo.InfoDac.Categoria, oVehiculo.TipoTarifa);
                            Tarifa oTarifa = ModuloBaseDatos.Instance.BuscarTarifa(oTarifaABuscar);
                            oVehiculo.CategoDescripcionLarga = oTarifa.Descripcion;
                            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.Violacion, null, null, oVehiculo);
                        }
                    }

                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(oVehiculo, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ACTUALIZA_ULTVEH, listaDatosVia);

                    ModuloPantalla.Instance.LimpiarVehiculo(oVehiculo);
                }
                else
                {
                    if (oVehiculo.SalioChupado)
                    {
                        _logger.Info("Violacion->Salida de Vehiculo chupado");
                        EnviarEventoMarchaAtras(ref oVehiculo, DateTime.Now, oVehiculo.Categoria, "CH");
                    }
                    else
                    {
                        _logger.Info("No se detectaron Peanas");
                    }
                    //Como no mandamos la violacion, lo limpiamos para que no se mande despues
                    oVehiculo = new Vehiculo();
                }

                _estadoAbort = 'V';
                _flagIniViol = false;

                _loggerTransitos?.Info($"V;{T1.ToString("HH:mm:ss.ff")};{oVehiculo.InfoDac.Categoria};{oVehiculo.NumeroVehiculo};{oVehiculo.NumeroTransito}");

                GrabarVehiculos();
                if (_logicaCobro.Estado != eEstadoVia.EVCerrada)
                    Autotabular();

                ModuloBaseDatos.Instance.AlmacenarTransitoTurno(oVehiculo, _logicaCobro.GetTurno);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
            oVehiculo.ProcesandoViolacion = false;
            _logger.Info("Violacion -> Fín");
            return ret;
        }

        public override void IniciarTimerApagadoCampanaPantalla(int? tiempoMseg)
        {
            if (tiempoMseg != null || tiempoMseg > 0)
            {
                //Timer de apagado de mimico de campana de violacion
                _timerApagadoCampana.Elapsed -= new ElapsedEventHandler(TimerApagadoCampanaPantalla);
                _timerApagadoCampana.Elapsed += new ElapsedEventHandler(TimerApagadoCampanaPantalla);
                _timerApagadoCampana.Interval = (int)tiempoMseg;
                _timerApagadoCampana.AutoReset = true;
                _timerApagadoCampana.Enabled = true;

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
            }
        }

        private void TimerApagadoCampanaPantalla(object source, ElapsedEventArgs e)
        {
            if (DAC_PlacaIO.Instance.ObtenerEstadoAlarma(eAlarma.Sonora) == enmEstadoAlarma.Ok &&
                DAC_PlacaIO.Instance.ObtenerEstadoAlarma(eAlarma.Exento) == enmEstadoAlarma.Ok)
            {
                _timerApagadoCampana.Enabled = false;
                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                mimicos.CampanaViolacion = enmEstadoAlarma.Ok;

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
            }
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

        private void SensorBidiAlto()
        {
            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
            _logger.Info("SensorBidiAlto -> BARRERA ARRIBA!!");
            // Actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaOpuestaAbierta);

            // Actualiza estado de turno en pantalla
            if (_logicaCobro.GetTurno.EstadoTurno == enmEstadoTurno.Cerrada)
            {
                _sentidoOpuesto = true;
                _ultimoSentidoEsOpuesto = true;
                listaDatosVia.Clear();
                Turno oTurno = new Turno();
                oTurno.AperturaOpuesta = true;
                ClassUtiles.InsertarDatoVia(oTurno, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);
            }
        }
        private void SensorBidiBajo()
        {
            DAC_PlacaIO.Instance.NuevoTransitoDAC(0, true);
            _sentidoOpuesto = false;
        }

        private void OnPlatOpen()
        {
            try
            {
                _logger.Info("Ing OnPlatOpen");
                FallaCritica oFallaCritica = new FallaCritica();
                oFallaCritica.CodFallaCritica = EnmFallaCritica.FCPlatAbie;
                oFallaCritica.Observacion = "A";

                ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, null);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        private void OnPlatClose()
        {
            try
            {
                _logger.Info("Ing OnPlatClose");
                FallaCritica oFallaCritica = new FallaCritica();
                oFallaCritica.CodFallaCritica = EnmFallaCritica.FCPlatAbie;
                oFallaCritica.Observacion = "C";

                ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, null);
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
            string csDacTot = string.Empty;
            int flag = 0;
            char Moto = 'N';    //Indica si el tránsito es una moto
            byte max1 = 0;
            byte max2 = 0;
            byte maxtot = 0;
            byte DACalt = 0;

            byte byCantEjes1 = 0, byCantEjes2 = 0, byCantEjes3 = 0, byCantEjes4 = 0, byCantEjes5 = 0, byCantEjes6 = 0, byCantEjes7 = 0, byCantEjes8 = 0;
            byte byRueDobles57 = 0, byRueDobles68 = 0, byAdelante57 = 0, byAdelante68 = 0, byAtras57 = 0, byAtras68 = 0;
            byte byCambSenti57 = 0, byCambSenti68 = 0, byAltura = 0;
            bool bOKEjes1 = false, bOKEjes2 = false, bOKEjes3 = false, bOKEjes4 = false;
            bool bUso1 = false;

            //Analizo la configuración de la via
            char cejes = ' ';
            char cdobl = ' ';
            char altur = ' ';

            byte btDACrdobles = 0;  //Contador de ruedas dobles 57 
            byte btDACrdobles2 = 0; //Contador de ruedas dobles 68
            byte btDACejes = 0;     //Contador de ejes 1
            byte btDACejes2 = 0;        //Contador de ejes 2
            byte btDACejes3 = 0;        //Contador de ejes 3
            byte btDACejes4 = 0;        //Contador de ejes 4
            byte btBus_Cejes = 0;       //Cantidad de ejes
            byte btBus_Rdobles = 0; //Cantidad de ruedas duales
            char btBus_Alt = 'B';       //Altura del vehículo
            short shCatego_Dac = 0; //Categoría del DAC
            bool bHayPeanas = false;    //Hay deteccion de alguna peana?

            _contSuspensionTurno = 0;

            _logger.Info("CategoriaPIC -> Inicio");

            //Si el vehiculo ya tiene asignada fecha, uso esa fecha para los eventos
            if (oVehiculo.Fecha > DateTime.MinValue)
                T1 = oVehiculo.Fecha;

            //Si el modo es D o autotabulante la vía no tiene peanas, no consulto al DAC y devuelvo
            //como categoría del DAC el valor de la categoría autotabulada
            if (/*m_sModo=="D" ||*/ _sinPeanas)
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

                if (oRegDac == null)
                {
                    _logger.Info("CategoriaPIC -> Modo<>D y no autotabulante, consulto DAC");
                    // @TODO
                    // Falta descontar los ejes para atras

                    ContadoresDAC contadores = new ContadoresDAC();

                    //Analizo la cantidad de contadores de ejes
                    short resp = DAC_PlacaIO.Instance.ObtenerContadores(cejes, cdobl, altur, ref contadores, 'S');
                    if (resp < 0)
                    {
                        //Si fallo la consulta, intento nuevamente.
                        _logger.Info("CategoriaPIC -> Fallo consulta DAC, reintento. Error [{0}]", resp);
                        resp = DAC_PlacaIO.Instance.ObtenerContadores(cejes, cdobl, altur, ref contadores, 'S');
                    }
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

                if (byCantEjes1 == byCantEjes5 && byCantEjes1 == byCantEjes7)
                {
                    bOKEjes1 = true;
                    /*
                                        if (byAdelante57 > byAtras57)
                                            byCantEjes1 = byAdelante57 - byAtras57;
                                        else
                                            byCantEjes1 = 0;
                    */
                }

                if (byCantEjes2 == byCantEjes6 && byCantEjes2 == byCantEjes8)
                {
                    bOKEjes2 = true;
                    /*
                                        if (byAdelante68 > byAtras68)
                                            byCantEjes2 = byAdelante68 - byAtras68;
                                        else
                                            byCantEjes2 = 0;
                    */
                }

                if (byCantEjes3 == byCantEjes5 && byCantEjes3 == byCantEjes7)
                {
                    bOKEjes3 = true;
                    /*
                                        if (byAdelante57 > byAtras57)
                                            byCantEjes3 = byAdelante57 - byAtras57;
                                        else
                                            byCantEjes3 = 0;
                    */
                }

                if (byCantEjes4 == byCantEjes6 && byCantEjes4 == byCantEjes8)
                {
                    bOKEjes4 = true;
                    /*
                                        if (byAdelante68 > byAtras68)
                                            byCantEjes4 = byAdelante68 - byAtras68;
                                        else
                                            byCantEjes4 = 0;*/
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

                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes > 0)
                                    {
                                        //ModuloEventos.Instance.SetErrorAsync(_logicaCobro.GetTurno,new EventoError() { })

                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                        oEventoError.Valor = 0;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);

                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 0);

                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 0";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }

                                }

                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 0);
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
                                if (bGenerarFallas)
                                {
                                    if (btDACejes > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                        oEventoError.Valor = btDACejes;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 1";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);
                            }
                            if (btDACejes2 < 2)
                            {

                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes2 > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                        oEventoError.Valor = btDACejes2;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 3";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }

                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 3);
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

                                    if (bGenerarFallas)
                                    {
                                        if (btDACejes2 > 1)
                                        {
                                            //Mando el error del contador 3 que es DACejes2
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                            oEventoError.Valor = btDACejes2;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                    }
                                }
                                else
                                {
                                    btBus_Cejes = btDACejes2;

                                    if (bGenerarFallas)
                                    {
                                        if (btDACejes > 1)
                                        {
                                            //Mando el error del contador 1 que es DACejes
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                            oEventoError.Valor = btDACejes;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                    }
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
                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                        oEventoError.Valor = btDACejes;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else if (Moto != 'S')
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                        //Si es una moto no es una falla del sensor
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 1";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);
                            }
                            if (btDACejes2 < 2)
                            {

                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes2 > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                        oEventoError.Valor = btDACejes2;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else if (Moto != 'S')
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 2);
                                        //Si es una moto no es una falla del sensor
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 2";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 2);
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

                                        if (bGenerarFallas)
                                        {
                                            if (btDACejes2 > 1)
                                            {
                                                //Mando el error del contador 2 que es DACejes2
                                                EventoError oEventoError = new EventoError();
                                                oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                                oEventoError.Valor = btDACejes2;
                                                oEventoError.Observacion = "";

                                                ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        btBus_Cejes = btDACejes2;

                                        if (bGenerarFallas)
                                        {
                                            if (btDACejes > 1)
                                            {
                                                //Mando el error del contador 1 que es DACejes
                                                EventoError oEventoError = new EventoError();
                                                oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                                oEventoError.Valor = btDACejes;
                                                oEventoError.Observacion = "";

                                                ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                            }
                                        }

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
                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                        oEventoError.Valor = btDACejes;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 1";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);
                            }
                            if (btDACejes2 < 2)
                            {
                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes2 > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                        oEventoError.Valor = btDACejes2;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 2);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 2";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 2);
                            }
                            if (btDACejes3 < 2)
                            {

                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes3 > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                        oEventoError.Valor = btDACejes3;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 3";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 3);
                            }
                            if (btDACejes4 < 2)
                            {
                                if (bGenerarFallas)
                                {
                                    //Generar evento de falla de contador
                                    if (btDACejes4 > 0)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_4;
                                        oEventoError.Valor = btDACejes4;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 4);
                                        FallaCritica oFallaCritica = new FallaCritica();
                                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                        oFallaCritica.Observacion = "Sensor de ejes: 4";

                                        ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                    }
                                }
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 4);
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

                                if (bGenerarFallas)
                                {
                                    if (btDACejes != btBus_Cejes && btDACejes > 0)
                                    {
                                        //Mando falla en este contador
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                        oEventoError.Valor = btDACejes;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    if (btDACejes2 != btBus_Cejes && btDACejes2 > 0)
                                    {
                                        //Mando falla en este contador
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                        oEventoError.Valor = btDACejes2;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    if (btDACejes3 != btBus_Cejes && btDACejes3 > 0)
                                    {
                                        //Mando falla en este contador
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                        oEventoError.Valor = btDACejes3;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
                                    if (btDACejes4 != btBus_Cejes && btDACejes4 > 0)
                                    {
                                        //Mando falla en este contador
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.SensorEjes_4;
                                        oEventoError.Valor = btDACejes4;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
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
                                        if (bGenerarFallas)
                                        {
                                            //Falla en la pedalera 1
                                            if (btDACejes > 0)
                                            {
                                                EventoError oEventoError = new EventoError();
                                                oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                                oEventoError.Valor = btDACejes;
                                                oEventoError.Observacion = "";

                                                ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);

                                            }
                                            else
                                            {
                                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                                FallaCritica oFallaCritica = new FallaCritica();
                                                oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                                oFallaCritica.Observacion = "Sensor de ejes: 1";

                                                ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);

                                            }
                                        }
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                    }
                                    if (btDACejes3 < 2)
                                    {

                                        if (bGenerarFallas)
                                        {
                                            //Falla en la pedalera 3
                                            if (btDACejes3 > 0)
                                            {
                                                EventoError oEventoError = new EventoError();
                                                oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                                oEventoError.Valor = btDACejes3;
                                                oEventoError.Observacion = "";

                                                ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                            }
                                            else
                                            {
                                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                                FallaCritica oFallaCritica = new FallaCritica();
                                                oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                                oFallaCritica.Observacion = "Sensor de ejes: 3";

                                                ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                    }
                                }
                                else
                                {
                                    if (bGenerarFallas)
                                    {
                                        //Piso la pedalera 2
                                        //Si conto 1 eje hubo un fallo en la pedalera
                                        if (btDACejes2 < 2)
                                        {
                                            if (btDACejes2 > 0)
                                            {
                                                EventoError oEventoError = new EventoError();
                                                oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                                oEventoError.Valor = btDACejes2;
                                                oEventoError.Observacion = "";

                                                ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                            }
                                            else
                                            {
                                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 2);
                                                FallaCritica oFallaCritica = new FallaCritica();
                                                oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                                oFallaCritica.Observacion = "Sensor de ejes: 2";

                                                ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {   //NO ES UNA MOTO
                                //Si todos contaron menos de 2 ejes no se detecto
                                //un vehiculo
                                if ((btDACejes < 2) && (btDACejes2 < 2) && (btDACejes3 < 2))
                                {
                                    btBus_Cejes = 0;

                                    if (bGenerarFallas)
                                    {
                                        //Grabo un error con los valores detectados por
                                        //cada pedalera
                                        if (btDACejes > 0)
                                        {
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                            oEventoError.Valor = btDACejes;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                        else
                                        {
                                            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 1";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                        if (btDACejes2 > 0)
                                        {
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                            oEventoError.Valor = btDACejes2;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                        else
                                        {
                                            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 2);
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 2";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                        if (btDACejes3 > 0)
                                        {
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                            oEventoError.Valor = btDACejes3;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                        else
                                        {
                                            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 3";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                    }
                                }
                                else
                                {
                                    if ((btDACejes == btDACejes2) && (btDACejes == btDACejes3))
                                    {
                                        //Si los contadores dieron igual, mando cualquiera de los 3
                                        btBus_Cejes = btDACejes;

                                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);
                                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 2);
                                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 3);
                                    }
                                    else
                                    {   //Me fijo si DACejes es el mayor
                                        if ((btDACejes >= btDACejes2) && (btDACejes >= btDACejes3))
                                        {
                                            //DACejes tiene el mayor valor
                                            btBus_Cejes = btDACejes;
                                            ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 1);


                                            if (bGenerarFallas)
                                            {
                                                if (btDACejes2 < btDACejes)
                                                {
                                                    //Mando el error del contador 2 que es DACejes2
                                                    EventoError oEventoError = new EventoError();
                                                    oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                    oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                                    oEventoError.Valor = btDACejes2;
                                                    oEventoError.Observacion = "";

                                                    ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                                }
                                                if (btDACejes3 < btDACejes)
                                                {
                                                    //Mando el error del contador 3 que es DACejes3
                                                    EventoError oEventoError = new EventoError();
                                                    oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                    oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                                    oEventoError.Valor = btDACejes3;
                                                    oEventoError.Observacion = "";

                                                    ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                                }
                                            }

                                        }
                                        else
                                        {   //Me fijo si DACejes2 es el mayor
                                            if ((btDACejes2 >= btDACejes) && (btDACejes2 >= btDACejes3))
                                            {   //DACEjes2 es el mayor
                                                btBus_Cejes = btDACejes2;

                                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 2);

                                                if (bGenerarFallas)
                                                {
                                                    //Me fijo si fallo la pedalera 1
                                                    if (btDACejes < btDACejes2)
                                                    {
                                                        //Mando el error del contador 1 que es DACejes
                                                        EventoError oEventoError = new EventoError();
                                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                                        oEventoError.Valor = btDACejes;
                                                        oEventoError.Observacion = "";

                                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);

                                                    }
                                                    //Me fijo si fallo la pedalera 3
                                                    if (btDACejes3 < btDACejes2)
                                                    {
                                                        //Mando el error del contador 3 que es DACejes3
                                                        EventoError oEventoError = new EventoError();
                                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                        oEventoError.Sensor = eSensorEvento.SensorEjes_3;
                                                        oEventoError.Valor = btDACejes2;
                                                        oEventoError.Observacion = "";

                                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                                    }
                                                }
                                            }
                                            else
                                            {   //DACEjes3 es el mayor
                                                btBus_Cejes = btDACejes3;

                                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.FailSensPiso, 3);

                                                if (bGenerarFallas)
                                                {
                                                    //Me fijo si fallo la pedalera 1
                                                    if (btDACejes < btDACejes3)
                                                    {
                                                        //Mando el error del contador 1 que es DACejes
                                                        EventoError oEventoError = new EventoError();
                                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                        oEventoError.Sensor = eSensorEvento.SensorEjes_1;
                                                        oEventoError.Valor = btDACejes;
                                                        oEventoError.Observacion = "";

                                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                                    }
                                                    //Me fijo si falló la pedalera 2
                                                    if (btDACejes2 < btDACejes3)
                                                    {
                                                        //Mando el error del contador 2 que es DACejes2
                                                        EventoError oEventoError = new EventoError();
                                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                                        oEventoError.Sensor = eSensorEvento.SensorEjes_2;
                                                        oEventoError.Valor = btDACejes;
                                                        oEventoError.Observacion = "";

                                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                                    }
                                                }

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
                ///////////////
                //ALTURA //////
                ///////////////
                switch (altur)
                {
                    case 'S':
                        {
                            //Si tiene altura y esta en fallo, genero evento
                            _falloAltura = DAC_PlacaIO.Instance.ObtenerFalloAltura();

                            /*if (_falloAltura)
                            {
                                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.Altura, 0);
                            }
                            else
                            {
                                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.Altura, 0);
                            }*/

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
                //DOBLES///////
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
                            //if (btBus_Cejes > 2)
                            {
                                if (byCantEjes1 > 0 || byCantEjes5 > 0 || byCantEjes7 > 0)
                                {
                                    valor = byCantEjes5;

                                    if (bGenerarFallas && oRegDac == null) //NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 5";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                    }

                                    valor = byCantEjes7;

                                    if (bGenerarFallas && oRegDac == null) //NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 7";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                    }
                                }

                                if (byCantEjes2 > 0 || byCantEjes6 > 0 || byCantEjes8 > 0)
                                {
                                    valor = byCantEjes6;

                                    if (bGenerarFallas && oRegDac == null) //NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 6";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                    }

                                    valor = byCantEjes8;

                                    if (bGenerarFallas && oRegDac == null) //NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 8";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }

                                    }

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
                            //if (btBus_Cejes > 2)
                            {
                                if (byCantEjes1 > 0 || byCantEjes5 > 0 || byCantEjes7 > 0)
                                {
                                    valor = byCantEjes5;

                                    if (bGenerarFallas && oRegDac == null)//NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 5";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                        else if (valor == 1 && Moto != 'S')
                                        {
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorRuedasDobles_5;
                                            oEventoError.Valor = valor;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                    }

                                    valor = byCantEjes7;

                                    if (bGenerarFallas && oRegDac == null)//NO para transito md (no guardo estos cont)
                                    {
                                        if (valor == 0 && Moto != 'S')
                                        {
                                            FallaCritica oFallaCritica = new FallaCritica();
                                            oFallaCritica.CodFallaCritica = EnmFallaCritica.FCSensor;
                                            oFallaCritica.Observacion = "Sensor de ejes: 7";

                                            ModuloEventos.Instance.SetFallasCriticas(_logicaCobro.GetTurno, oFallaCritica, oVehiculo);
                                        }
                                        else if (valor == 1 && Moto != 'S')
                                        {
                                            EventoError oEventoError = new EventoError();
                                            oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                            oEventoError.Sensor = eSensorEvento.SensorRuedasDobles_7;
                                            oEventoError.Valor = valor;
                                            oEventoError.Observacion = "";

                                            ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                        }
                                    }

                                    if (byCantEjes5 != byCantEjes7)
                                    {
                                        EventoError oEventoError = new EventoError();
                                        oEventoError.CodigoError = eCodErrorEvento.SensoresDAC;
                                        oEventoError.Sensor = eSensorEvento.DetectorRuedasDobles_5_7;
                                        oEventoError.Valor = 0;
                                        oEventoError.Observacion = "";

                                        ModuloEventos.Instance.SetError(_logicaCobro.GetTurno, oEventoError, oVehiculo);
                                    }
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
                {   //Es una moto, limpio el contador de ejes
                    //btBus_Cejes=0;
                }
                else
                {   //No es moto, saco el flag de moto
                    Moto = 'N';
                }

                //Si detecto un solo eje lo buscamos como 2 ejes
                if (/*((btBus_Cejes < 1) && (Moto == 'N')) ||*/ (btBus_Cejes > 9))
                {
                    //No categorizo falla en sensor
                    if (btBus_Cejes < 2)
                    {
                        shCatego_Dac = -2;
                    }

                    //No categorizo falla en lazo
                    if (btBus_Cejes > 9)
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
                    else if (btBus_Cejes == 1)
                        auxBusejes = 2;
                    else
                        auxBusejes = btBus_Cejes;

                    //Si no tiene detector duales y fallo la altura
                    //reviso si con y sin altura es la misma categoria
                    short categoria = 0;
                    if (cdobl == '0' && _falloAltura)
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
                        {
                            shCatego_Dac = categoria;
                        }
                        else
                        {
                            //Si falla la consulta no limpio la deteccion del DAC.
                            shCatego_Dac = categoria;
                            _logger.Info("Error al buscar Categorias DAC en la base local");
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
        /// Mueve el vehículo correspondiente de acuerdo al tipo de movimiento
        /// No tiene efecto cuando la vía está en modo D
        /// </summary>
        /// <param name="eMovim">tipo de movimiento</param>
        public override void AdelantarVehiculo(eMovimiento movimiento)
        {
            //No es válido para el modo D


            if (movimiento == eMovimiento.eCierreTurno)
            {
                //Vacío la vía generando op cerradas y marcha atrás según corresponda
                //Genero un evento si C0 está ocupado
                GenerarEventoSiOcupado(eVehiculo.eVehC0);

                //Genero un evento si C1 está ocupado
                GenerarEventoSiOcupado(eVehiculo.eVehC1);

                //Si P3 está ocupado y tiene forma de pago válida genero un evento de Op. cerrada
                //por marcha atrás
                OpCerradaReversa(eVehiculo.eVehP3);
                //Idem P2
                OpCerradaReversa(eVehiculo.eVehP2);
                //Idem P1
                OpCerradaReversa(eVehiculo.eVehP1);
            }
            else
            {
                if (_logicaCobro.Estado == eEstadoVia.EVAbiertaPag)
                {
                    GetVehOnline().SetDatosPago(GetVehiculo(GetVehiculoPrimero()));
                }
                UpdateOnline();
                if (movimiento == eMovimiento.eOpCerrada)
                    RegularizarFilaVehiculos();
            }
        }

        override public void ActualizarEstadoSensoresEscape()
        {
            Sensor sensor = null;

            // BPA
            sensor = new Sensor();
            sensor.CodigoSensor = "LAZ";
            sensor.NumeroSensor = 3;
            sensor.Estado = DAC_PlacaIO.Instance.EstaOcupadoLazoEscape() ? "O" : "L";
            ModuloEventos.Instance.ActualizarSensores(sensor, true);

            // Barrera de salida
            sensor = new Sensor();
            sensor.CodigoSensor = "BAR";
            sensor.NumeroSensor = 2;

            if (DAC_PlacaIO.Instance.ObtenerBarreraSalida(eTipoBarrera.Escape) == enmEstadoBarrera.Nada)
                sensor.Estado = "";
            else if (DAC_PlacaIO.Instance.ObtenerBarreraSalida(eTipoBarrera.Escape) == enmEstadoBarrera.Arriba)
                sensor.Estado = "L";
            else if (DAC_PlacaIO.Instance.ObtenerBarreraSalida(eTipoBarrera.Escape) == enmEstadoBarrera.Abajo)
                sensor.Estado = "B";

            ModuloEventos.Instance.ActualizarSensores(sensor, true);
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

        override public void ActualizarEstadoSensores(bool joinThread = false)
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

        private void UpdateOnline()
        {
            ModuloEventos.Instance.ActualizarVehiculoOnline(GetVehOnline());
            //ModuloEventos.Instance.ActualizarTurno( _logicaCobro.GetTurno );//Lo hace logica Cobro
            ModuloEventos.Instance.SetEstadoOnline();
        }

        public override bool ViaVacia()
        {
            int i;
            bool bRet = true;		//bRet es true si está vacía
            i = (int)eVehiculo.eVehP3;
            while (bRet && i <= (int)eVehiculo.eVehC0)
            {
                //Si hay un vehiculo "Ocupado", la via no está vacía
                if (GetVehiculo((eVehiculo)i).Ocupado)
                    bRet = false;
                i++;
            }

            return bRet;
        }

        public override bool ViaSinVehPagados()
        {
            int i;
            bool bRet = false;		//bRet es true si está vacía
            i = (int)eVehiculo.eVehP3;
            while (!bRet && i <= (int)eVehiculo.eVehC0)
            {
                //Si hay uno Pagado no está vacía
                if (GetVehiculo((eVehiculo)i).EstaPagado)
                    bRet = true;
                i++;
            }

            //Si ya estamos abriendo, no esta vacia
            //if (_logicaCobro.EstoyAbriendoTurno)
            //    bRet = true;

            return bRet;

        }


        /// <summary>
        /// Devuelve el eVehiculo del primer vehiculo de la via
        /// </summary>
        /// <returns></returns>
        eVehiculo GetVehiculoPrimero()
        {
            eVehiculo eRet;
            int i;
            bool bEnc;

            //Empiezo a buscar desde C0 a P3 el vehículo que esté ocupado y no tenga
            //forma de pago
            i = (int)eVehiculo.eVehC0;
            bEnc = false;
            while (!bEnc && i >= (int)eVehiculo.eVehP3)
            {
                if (GetVehiculo((eVehiculo)i).NoVacio)
                    bEnc = true;
                if (!bEnc)
                    i--;
            }

            if (bEnc)
                eRet = (eVehiculo)i;
            else
                eRet = eVehiculo.eVehP1;

            return eRet;
        }

        /// <summary>
        /// Calcula la velocidad en que pasa un auto en la vía dinámica
        /// tomando como datos el tiempo(PIC) y la distancia(CONFIGV).
		/// Se toman los siguientes sensores del DAC |BPR| ====== |BPI|
		/// Distancia	= cm
        /// Tiempo = mseg
        /// </summary>
        /// <param name="shTiempo_mSeg">Tiempo entre BPR - BPI</param>
        /// <returns>Velocidad en Km/h (si es Mayor a 255 retorna 0)</returns>
        private byte GetVelocidad(short shTiempo_mSeg)
        {
            byte ret = 0;
            try
            {
                if (shTiempo_mSeg > 0)
                {
                    // Velocidad = Distancia (en cm) / tiempo (en mSegs) * 3600 segundos/hora / 100 cm/mt * 1000 mSegs/segundo / 1000 mts/km
                    ret = (byte)(((3600 / 100) * 20) / shTiempo_mSeg); //TODO IMPLEMENTAR distacia desde la base de datos ConfigV.GetDistancia()

                    //Se quita el redondeo ZUL 24/11/06
                    //Redondeo a cinco
                    //shRet = ((short) ((shRet + 2) / 5) * 5);

                    // Si dá mas de MAX_BYTE le asigno MAX_BYTE 
                    // para que no lo trunque luego al asignarle al vehículo
                    if (ret > 255)
                        ret = 0;
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e, "Error al obtener la velocidad");
            }


            return ret;
        }



        #region TIMERS
        private void onTimeoutAntena(object source, ElapsedEventArgs e)
        {
            _logger.Info("Desactivo Antena Por Timeout");
            DesactivarAntena(eCausaDesactivacionAntena.Tiempo);
        }

        private void onDesactivarAntena(object source, ElapsedEventArgs e)
        {
            _logger.Info("Desactivo Antena Por Sensores");
            DesactivarAntena(eCausaDesactivacionAntena.Sensores);
        }
        private void onObtenerVelocidad(object source, ElapsedEventArgs e)
        {
            //_logger.Debug("onObtenerVelocidad");


            GetVehiculo(eVehiculo.eVehC1).Velocidad = GetVelocidad(DAC_PlacaIO.Instance.GetBuclesTime());
            //Forzamos la muestra para que actualice la pantalla
            SetVehiculoIng(false, true);
            _logger.Info("OnTimer -> Velocidad [{Name} Km/h]", GetVehiculo(eVehiculo.eVehC1).Velocidad);
        }

        private void OnTimerBarreraLevantada(object source, ElapsedEventArgs e)
        {
            if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
                ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.BarreraLevantadaTiempoMax, 0);
            else
                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.BarreraLevantadaTiempoMax, 0);
        }

        private void EnviarFilaVehiculosPantalla(bool Iniciar)
        {
            if (Iniciar)
                _timerFilaVehiculos.Start();
            else
                _timerFilaVehiculos.Stop();
        }

        private void OnTimerFilaVehiculos(object source, ElapsedEventArgs e)
        {
            FilaVehiculos filaVehiculos = new FilaVehiculos();
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            Vehiculo veh;

            try
            {
                for (int i = (int)eVehiculo.eVehP3; i <= (int)eVehiculo.eVehC0; i++)
                {
                    veh = GetVehiculo((eVehiculo)i);
                    filaVehiculos.fila.Add((eVehiculo)i, veh);
                }

                filaVehiculos.ListaTagsLeidos = _lstTagLeidoAntena;
                ClassUtiles.InsertarDatoVia(filaVehiculos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FILAVEHICULOS, listaDatosVia);
            }
            catch
            {

            }
        }

        public override void LoguearColaVehiculos()
        {
            StringBuilder sb = new StringBuilder();

            FilaVehiculos filaVehiculos = new FilaVehiculos();

            sb.Append("\n");

            foreach (eVehiculo vehiculo in Enum.GetValues(typeof(eVehiculo)))
            {
                string ocupado = _vVehiculo[(int)vehiculo].Ocupado ? "S" : "N";
                string pagado = _vVehiculo[(int)vehiculo].EstaPagado ? "T" : "F";
                string tieneRecarga = _vVehiculo[(int)vehiculo].ListaRecarga.Any(x => x != null) ? "T" : "F";

                sb.Append($"Veh[{vehiculo.ToString().PadRight(15, ' ')}] - Nro[{_vVehiculo[(int)vehiculo].NumeroVehiculo.ToString().PadRight(8, ' ')}] - Cat[{_vVehiculo[(int)vehiculo].Categoria.ToString().PadRight(2, ' ')}] - Pag[{pagado}] - TipOp[{_vVehiculo[(int)vehiculo].TipOp.ToString().PadRight(2, ' ')}] - Tag[{_vVehiculo[(int)vehiculo].InfoTag?.NumeroTag?.Trim()}] - Pat[{_vVehiculo[(int)vehiculo].InfoOCRDelantero?.Patente}] - NroTr[{_vVehiculo[(int)vehiculo].NumeroTransito}] - Ocu[{ocupado}] - Rec[{tieneRecarga}]\n");

                if ((int)vehiculo >= (int)eVehiculo.eVehP3 && (int)vehiculo <= (int)eVehiculo.eVehC0)
                    filaVehiculos.fila.Add(vehiculo, _vVehiculo[(int)vehiculo]);
            }

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(filaVehiculos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FILAVEHICULOS, listaDatosVia);

            _logger.Info(sb.ToString());
        }

        public override void LimpiarColaVehiculos()
        {
            foreach (eVehiculo vehiculo in Enum.GetValues(typeof(eVehiculo)))
            {
                _vVehiculo[(int)vehiculo] = new Vehiculo();
            }
        }

        /// <summary>
        /// Agrega los datos del tag leído al elemento más antiguo (Head)
        /// y si ese elemento tiene 1 solo vehículo llama a AsignarTagAVehiculo
        /// Si es una lectura manual no se busca en la lista de tags leídos
        /// y se asigna al vehículo ING
        /// </summary>
        /// <param name="oInfoTag">datos del tag devuelto por la antena</param>
        /// <param name="bLecturaManual">flag para indicar lectura manual</param>
        /// <param name="bNoAsignarAVehiculo"></param>
        /// <returns>True si la lista no está vacía. False si la lista está vacía</returns>
        private bool AsignarTagLeido(InfoTag oInfoTag, bool bLecturaManual, bool bNoAsignarAVehiculo, ulong ulNumVeh = 0)
        {
            bool bRet = false, bEnc = false;
            int i;
            ulong ulNroVehiculo = 0;
            Vehiculo oVeh = null;

            lock (_lockAsignarTagLeido)
            {
                _logger.Info("AsignarTagLeido -> Inicio. Tag:[{Name}] LecturaManual:[{Name}] NoAsignarAVehiculo:[{Name}] TipOp[{Name}]", oInfoTag.NumeroTag, bLecturaManual, bNoAsignarAVehiculo, oInfoTag.TipOp);

                oVeh = GetVehIng();

                //Si el tag está habilitado lo tengo que sumar en el turno, si se encola ya está descontado el saldo
                if (oInfoTag.GetTagHabilitado() /*&& !oVeh.CobroEnCurso && !oVeh.EstaPagado*/ )
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
                        if (oVeh == GetVehiculo(eVehiculo.eVehP1))
                            AsignarNumeroVehiculo(eVehiculo.eVehP1);
                        else if (oVeh == GetVehiculo(eVehiculo.eVehP2) && !GetVehiculo(eVehiculo.eVehP1).NoVacio)
                            AsignarNumeroVehiculo(eVehiculo.eVehP2);
                    }

                    //Asigno el tag al vehículo Ing
                    _logger.Info("AsignarTagLeido -> Asigno tag Tag:[{Name}], Vehículo:[{Name}]", oInfoTag.NumeroTag, oVeh.NumeroVehiculo);

                    AsignarTagAVehiculo(oInfoTag, oVeh.NumeroVehiculo);
                    //Si el tag está habilitado lo tengo que sumar en el turno
                    if (oInfoTag.GetTagHabilitado())
                    {
                        //Para que muestre los datos
                        //le indicamos que estamos cobrando en este momento
                        SetVehiculoIng(false, true, false, true);
                    }
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

                        //si se lee el tag y se asigna inmediatamente, se puede apagar la antena, solo si está habilitado o el cajero puede cobrarlo (sin saldo, pago en via)
                        if (oInfoTag.GetTagHabilitado() || oInfoTag.TagOK)
                        {
                            ModuloAntena.Instance.DesactivarAntena();
                            _logger.Info("AsignarTagLeido -> Desactivo Antena, Tag OK");
                        }

                        //Hay un solo vehículo, llamo a AsignarTagAVehiculo
                        AsignarTagAVehiculo(oInfoTag, ulNroVehiculo);
                    }
                    else
                    {
                        //Tiene más de un vehiculo o ninguno
                        //o el vehiculo no existe más
                        //lo agrego a la lista de pendientes
                        //if(ulNroVehiculo != 0)
                        {
                            //Si habia un solo vehiculo y no lo encontro ya no sirve mas
                            if (_infoTagLeido.GetCantVehiculos() == 1)
                                _infoTagLeido.BorrarListaVehiculos();
                            QueueTagLeido(_infoTagLeido);
                        }
                    }

                    //Ya el tag pasó a algun lado, limpiamos el dato del tag
                    //Si esta habilirado ya no asignamos más a estos vehiculos
                    if (oInfoTag.GetTagHabilitado())
                    {
                        _infoTagLeido.Clear();
                    }
                    else
                    {
                        //_infoTagLeido.Clear();
                        //_infoTagLeido.GetInfoTag().Clear();
                    }
                }
                _loggerTransitos?.Info($"P;{oVeh.Fecha.ToString("HH:mm:ss.ff")};{oVeh.Categoria};{oVeh.TipOp};{oVeh.TipBo};{oVeh.GetSubFormaPago()};{oVeh.Tarifa};{oVeh.NumeroTicketF};{oVeh.Patente};{oInfoTag.NumeroTag};{oInfoTag.Ruc};{0};{oVeh.NumeroVehiculo};{oVeh.NumeroTransito}", bLecturaManual == false ? "0" : "1");

                _logger.Info("AsignarTagLeido -> Fin");
            }
            return bRet;
        }


        /// <summary>
        /// Agrega los datos del tag a la cola de tags
        /// </summary>
        /// <param name="oInfoTagLeido">datos de un tag a encolar</param>
        private void QueueTagLeido(InfoTagLeido oInfoTagLeido)
        {
            string sAux = "";
            lock (_lockModificarListaTag)
            {
                InfoTagLeido infoTagLeido = new InfoTagLeido();

                infoTagLeido.OInfoTag = oInfoTagLeido.OInfoTag;

                _logger.Info("QueueTagLeido -> INICIO");
                // NBA No se setea en ningun lado, pero se usa en AsignarTagEnCola
                //_infoTagEnCola = infoTagLeido.OInfoTag;

                if (oInfoTagLeido.GetCantVehiculos() > 0)
                {
                    sAux = "QueueTagLeido -> Vehiculo:";
                    for (int i = 0; i < oInfoTagLeido.GetCantVehiculos(); i++)
                    {
                        infoTagLeido.AgregarVehiculo(oInfoTagLeido.GetNumeroVehiculo(i));
                        sAux += oInfoTagLeido.GetNumeroVehiculo(i).ToString() + ", ";
                    }
                    sAux = sAux.Substring(0, sAux.Length - 1);
                    _logger.Info(sAux);
                }

                //Agrego un nuevo tag a la cola de tags si no está en la lista
                bool bEnLista = false;
                try
                {
                    bEnLista = _lstInfoTagLeido.Any(f => f.GetInfoTag() == oInfoTagLeido.OInfoTag);
                }
                catch (Exception e)
                {
                    _logger.Error(e);
                    bEnLista = _lstInfoTagLeido.Contains(infoTagLeido);
                }

                if (!bEnLista)
                {
                    _lstInfoTagLeido.Add(infoTagLeido);
                    _logger.Info("QueueTagLeido -> Se agrega Tag: {0}", infoTagLeido.GetInfoTag().NumeroTag);
                }
                //Salvamos la nueva lista
                //GrabarTagsLeidos();
                _logger.Info("QueueTagLeido -> FIN");

            }
        }

        /// <summary>
        /// Encola el tag con una lista vacia
        /// </summary>
        /// <param name="oInfoTag"></param>
        private void QueueTagLeido(InfoTag oInfoTag)
        {
            lock (_lockModificarListaTag)
            {
                InfoTagLeido infoTagLeido = new InfoTagLeido();
                infoTagLeido.OInfoTag = oInfoTag;

                _logger.Debug("QueueTagLeido1 -> INICIO");

                //Agrego un nuevo tag a la cola de tags si no está en la lista
                bool bEnLista = false;
                try
                {
                    bEnLista = _lstInfoTagLeido.Any(f => f.GetInfoTag() == oInfoTag);
                }
                catch (Exception e)
                {
                    _logger.Error(e);
                    bEnLista = _lstInfoTagLeido.Contains(infoTagLeido);
                }

                if (!bEnLista)
                {
                    _lstInfoTagLeido.Add(infoTagLeido);
                    _logger.Info("QueueTagLeido1 -> Se agrega Tag: {0}", infoTagLeido.GetInfoTag().NumeroTag);
                }

                //Salvamos la nueva lista
                GrabarTagsLeidos();
                _logger.Debug("QueueTagLeido1 -> FIN");

            }
        }

        /// <summary>
        /// Si este vehiculo no tiene a nadie ocupado delante levanta la barrera
        ///Se ejecuta en el mismo Thread que la antena(para abrir la barrera lo antes posible)
        ///Se llama solo si el tag está habilitado
        ///Obtiene el numero de vehiculo de m_InfoTagLeido
        /// </summary>
        /// <returns>si abrio la barrera</returns>
        private bool RevisarBarreraTag()
        {
            bool bEnc = false, bRet = false;
            int i;
            ulong ulNroVehiculo = 0;
            eVehiculo vehiculo = eVehiculo.eVehOnLine;
            Vehiculo oVeh = null;

            _logger.Info("RevisarBarreraTag -> Inicio ");
            //Si hay 1 solo vehiculo posible
            if (_infoTagLeido.GetCantVehiculos() == 1)
            {
                ulNroVehiculo = _infoTagLeido.GetNumeroVehiculo(0);
                _logger.Info("RevisarBarreraTag -> Vehiculo [{Name}]", ulNroVehiculo);
                //Busco en la lista de vehículos el vehículo con número ulNroVehiculo
                bEnc = false;
                i = (int)eVehiculo.eVehP3;
                while (!bEnc && i <= (int)eVehiculo.eVehC0)
                {
                    oVeh = GetVehiculo((eVehiculo)i);
                    if (oVeh.NumeroVehiculo == ulNroVehiculo)
                    {
                        _logger.Debug("RevisarBarreraTag -> Encuentro vehiculo con el mismo numero, EstaPagado[{Name}], TagHabilitado[{Name}], PermiteTag[{Name}]", oVeh.EstaPagado, oVeh.InfoTag.GetTagHabilitado(), oVeh.NoPermiteTag);
                        if (!oVeh.EstaPagado && !oVeh.InfoTag.GetTagHabilitado() && !oVeh.NoPermiteTag)
                            //Solo lo asigno si no está pagado
                            bEnc = true;
                    }
                    i++;
                }
                if (bEnc)
                {
                    i--;
                    vehiculo = (eVehiculo)i;
                    //Si el vehículo 
                    // es P1 y C1 y C0 están vacíos
                    //o es C1 y C0 está vacío
                    //o es C0		
                    if (
                        (vehiculo == eVehiculo.eVehP1 && !GetVehiculo(eVehiculo.eVehC1).NoVacio && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                        (vehiculo == eVehiculo.eVehC1 && !GetVehiculo(eVehiculo.eVehC0).NoVacio) ||
                        (vehiculo == eVehiculo.eVehC0))
                    {
                        //Limpio la cuenta de peanas del DAC
                        short Categoria = GetVehiculo(vehiculo).Categoria != 0 ? GetVehiculo(vehiculo).Categoria : _infoTagLeido.OInfoTag.Categoria;
                        DAC_PlacaIO.Instance.NuevoTransitoDAC(Categoria, EstaOcupadoBucleSalida() ? false : true);

                        DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                        GetVehiculo(vehiculo).BarreraAbierta = true; //indicar que ya abrió la barrera por tag hab

                        // Actualiza el estado de los mimicos en pantalla
                        Mimicos mimicos = new Mimicos();
                        DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                        GetVehOnline().InfoDac.Categoria = 0;

                        //Elimino del vehiculo las fotos de la lectura del tag
                        GetVehiculo(vehiculo).ListaInfoFoto.RemoveAll(x => x != null && (x.Causa == eCausaVideo.TagLeidoPorAntena || x.Causa == eCausaVideo.PagadoTelepeaje));
                        //Saco una foto del momento en el que llega el tag por antena y la agrego a InfoMedios
                        InfoMedios oFoto = new InfoMedios(ObtenerNombreFoto(eCamara.Frontal), eCamara.Frontal, eTipoMedio.Foto, eCausaVideo.PagadoTelepeaje);
                        ModuloFoto.Instance.SacarFoto(new Vehiculo(), eCausaVideo.PagadoTelepeaje, false, oFoto);
                        if (GetVehiculo(vehiculo).ListaInfoFoto.Any(x => x != null && x.Camara == eCamara.Frontal && x.Causa == eCausaVideo.PagadoTelepeaje))
                            GetVehiculo(vehiculo).ListaInfoFoto[GetVehiculo(vehiculo).ListaInfoFoto.FindIndex(x => x != null && x.Camara == eCamara.Frontal && x.Causa == eCausaVideo.PagadoTelepeaje)] = oFoto;
                        else
                            GetVehiculo(vehiculo).ListaInfoFoto.Add(oFoto);

                        _logger.Info("RevisarBarreraTag -> Tag habilitado, subo barrera T_Vehiculo:[{Name}]", vehiculo.ToString());
                    }
                    else
                        _logger.Debug("RevisarBarreraTag -> No vacio Vehiculo[{Name}]", vehiculo);
                }
            }

            _logger.Info("RevisarBarreraTag -> FIN Cant Vehiculos: {0}", _infoTagLeido.GetCantVehiculos());
            return bRet;
        }

        /// <summary>
        /// Setea la informacion de Vehiculo que proviene de la Base de Datos Local
        /// </summary>
        /// <param name="aVehiculo">Vehiculo almacenado en la BD</param>
        override public void SetVehiculo(Vehiculo[] aVehiculo)
        {
            _logger.Info("SetVehiculo -> Recupero fila de vehiculos desde BD local");
            // En el caso de que venga algun vehiculo en null
            for (int i = 0; i < aVehiculo.Length; i++)
            {
                if (aVehiculo[i] == null)
                    aVehiculo[i] = new Vehiculo();
            }
            //_vVehiculo = aVehiculo;   //Lo saco por ahora FAB
            LoguearColaVehiculos();
            SetVehiculoIng();
        }
        #endregion

        /// <summary>
        /// Limpia veh ing de la cola de vehiculos
        /// </summary>
        public override int LimpiarVehIng()
        {
            int i = (int)GetVehiculoIng();
            _vVehiculo[i] = new Vehiculo();
            return i;
        }

        public override void SetFlagOcupado(int nVeh, bool bOcup, ulong numeroVehiculo)
        {
            _vVehiculo[nVeh].Ocupado = bOcup;
            _vVehiculo[nVeh].NumeroVehiculo = numeroVehiculo;
        }

        /// <summary>
        /// Evalúa si el último tag cancelado leído por antena tiene la misma patente
        /// que el tag manual a procesar
        /// </summary>
        /// <param name="sPatente">Patente a comparar</param>
        /// <returns>True si la patente es igual, de lo contrario False</returns>
        public override bool UltimoAnuladoEsIgual(string sPatente)
        {
            return _ultTagCancelado.Patente == sPatente ? true : false;
        }

        /// <summary>
        /// Busca el vehiculo asociado al numero de vehiculo
        /// </summary>
        /// <param name="ulNroVehiculo">Numero del vehiculo</param>
        /// <returns>Posicion del vehiculo</returns>
        public override eVehiculo BuscarVehiculo(ulong ulNroVehiculo, bool buscarNoPagado = false, bool buscarHastaC0 = false, string sNroTag = "")
        {
            Vehiculo oVeh = null;
            int i = (int)eVehiculo.eVehP3;
            int final = !buscarHastaC0 ? (int)eVehiculo.eVehObservado : (int)eVehiculo.eVehC0;
            bool encontrado = false;

            //Busco en la lista de vehículos el vehículo con número ulNroVehiculo
            while (i <= final)
            {
                oVeh = GetVehiculo((eVehiculo)i);
                if (oVeh.NumeroVehiculo == ulNroVehiculo && ulNroVehiculo > 0)
                {
                    encontrado = true;
                    break;
                }
                else if (!string.IsNullOrEmpty(sNroTag))
                {
                    if (oVeh.InfoTag != null && oVeh.InfoTag.NumeroTag == sNroTag)
                    {
                        encontrado = true;
                        break;
                    }
                }
                else if (ulNroVehiculo <= 0)
                {
                    if (!buscarNoPagado)
                    {
                        if (!oVeh.EstaPagado && !oVeh.InfoTag.GetTagHabilitado() && !oVeh.NoPermiteTag)
                        {
                            //Solo lo asigno si no está pagado
                            encontrado = true;
                            break;
                        }
                    }
                    else
                    {
                        if (oVeh.EstaPagado)
                        {
                            //Solo lo asigno si está pagado
                            encontrado = true;
                            break;
                        }
                    }
                }
                i++;
            }
            if (!encontrado)
                i = (int)eVehiculo.eVehP1;
            return (eVehiculo)i;
        }

        public override void Dispose()
        {
            _timerApagadoCampana?.Stop();
            _timerApagadoCampana?.Dispose();

            _timerBarreraLevantada?.Stop();
            _timerBarreraLevantada?.Dispose();

            _timerTimeoutAntena?.Stop();
            _timerTimeoutAntena?.Dispose();

            _timerDesactivarAntena?.Stop();
            _timerDesactivarAntena?.Dispose();

            _timerObtenerVelocidad?.Stop();
            _timerObtenerVelocidad?.Dispose();

            _timerBorrarTagsViejos?.Stop();
            _timerBorrarTagsViejos?.Dispose();

            _timerFilaVehiculos?.Stop();
            _timerFilaVehiculos?.Dispose();

            _timerEsperaPosOcr?.Stop();
            _timerEsperaPosOcr?.Dispose();

            _logger.Debug("Dispose -> Dispose Timers");
        }

    }
}
