using Alarmas;
using Comunicacion;
using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionAntena;
using Entidades.ComunicacionBaseDatos;
using Entidades.ComunicacionChip;
using Entidades.ComunicacionEventos;
using Entidades.ComunicacionFoto;
using Entidades.ComunicacionImpresora;
using Entidades.ComunicacionMonitor;
using Entidades.ComunicacionVideo;
using Entidades.ComunicacionTarjetaCredito;
using Entidades.Interfaces;
using Entidades.Logica;
using ModuloDAC_PLACAIO.Señales;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Utiles;
using Utiles.Utiles;

namespace ModuloLogicaCobro.LogicaCobro
{
    public class LogicaCobroManual : LogicaCobro
    {
        #region Variables y Propiedades de clase

        private ILogicaVia _logicaVia = null;

        private NLog.Logger _logger = NLog.LogManager.GetLogger("LogicaVia");
        private NLog.Logger _loggerExcepciones = NLog.LogManager.GetLogger("excepciones");
        private NLog.Logger _loggerTransitos = NLog.LogManager.GetLogger("Transitos");

        private int _minutosPrevioCierreJornada;
        private int _minutosCierreModoMantenimiento;
        private int _minimoEspacioLibreDiscoGB;
        private bool _bUltimoSentidoEsOpuesto = false;
        private int _nComitivaVehPendientes = 0;
        private bool _bSIPforzado = false;

        private System.Timers.Timer _timerFecha = new System.Timers.Timer();
        private System.Timers.Timer _timerChequeo = new System.Timers.Timer();
        private System.Timers.Timer _timerCambioTarifa = new System.Timers.Timer();
        private System.Timers.Timer _timerCambioJornada = new System.Timers.Timer();
        private System.Timers.Timer _timerControlCambioTarifa = new System.Timers.Timer();
        private System.Timers.Timer _timerConsultaParte = new System.Timers.Timer();
        private System.Timers.Timer _timerSeteoDAC = new System.Timers.Timer();
        private System.Timers.Timer _timerEstadoImp = new System.Timers.Timer();
        private System.Timers.Timer _timerEstadoOnline = new System.Timers.Timer();
        private System.Timers.Timer _timerServicioMonitor = new System.Timers.Timer();

        private System.Timers.Timer _timerCierreModoMantenimiento = new System.Timers.Timer();

        private System.Timers.Timer _timerConsultaPesos = new System.Timers.Timer();

        private object _mutexCambioTarifa = new object();
        private bool _reintentandoConsulta = false;
        private Operador _operadorActual = null;
        public override Operador GetOperador { get { return _operadorActual; } }

        List<int> _comandosSupervision = new List<int>();

        Causa _causaLogin = new Causa();

        private object _lockRevisaCambioTarifa = new object();
        private object _lockRevisaCambioTarifaTimer = new object();

        private UltimaAccion _ultimaAccion = new UltimaAccion();
        #endregion

        #region Constructor, seteo e inicializacion de datos

        override public eEstadoNumeracion GetEstadoNumeracion()
        {
            return _estadoNumeracion;
        }

        /// <summary>
        /// Constructor Logica Cobro Manual
        /// </summary>
        public LogicaCobroManual()
        {
            _logger?.Info("Inicia Constructor de LogicaCobroManual");

            _turno = new Turno();

            // Inicializacion de timers
            _timerChequeo.Elapsed += new ElapsedEventHandler(TimerChequeoTimeStamp);
            _timerChequeo.Interval = 20 * 1000;
            _timerChequeo.AutoReset = true;
            _timerChequeo.Enabled = false;

            _timerFecha.Elapsed += new ElapsedEventHandler(Timer1Segundo);
            _timerFecha.Interval = 1000;
            _timerFecha.AutoReset = true;

            _timerFecha.Enabled = false;

            //Timer de cambio de tarifa (ejecucion del cambio)
            _timerCambioTarifa.Elapsed += new ElapsedEventHandler(TimerCambioTarifaCierra);
            _timerCambioTarifa.Interval = 500;
            _timerCambioTarifa.AutoReset = false;
            _timerCambioTarifa.Enabled = false;

            _timerCambioJornada.Elapsed += new ElapsedEventHandler(TimerMsgCambioJornada);
            _timerCambioJornada.Interval = 10000;
            _timerCambioJornada.AutoReset = false;
            _timerCambioJornada.Enabled = false;

            //Timer de cambio de tarifa (control si hubo un cambio de tarifa)
            _timerControlCambioTarifa.Elapsed += new ElapsedEventHandler(TimerChequeoCambioTarifa);
            _timerControlCambioTarifa.Interval = 60000;  //Ejecuto cada 1 minuto
            _timerControlCambioTarifa.AutoReset = true;
            _timerControlCambioTarifa.Enabled = false;

            //Timer que ejecuta estado de la impresora cada minuto si la via esta cerrada
            _timerEstadoImp = new System.Timers.Timer();
            _timerEstadoImp.Elapsed += OnTimedEventEstadoImp;
            _timerEstadoImp.Interval = 60000;  //Ejecuto cada 1 minuto
            _timerEstadoImp.AutoReset = true;
            _timerEstadoImp.Enabled = true;

            // Timer que actualiza el estado online cada 30 segundos
            _timerEstadoOnline = new System.Timers.Timer();
            _timerEstadoOnline.Elapsed += OnTimedEventEstadoOnline;
            _timerEstadoOnline.Interval = 30000;  //Ejecuto cada 30 segundos
            _timerEstadoOnline.AutoReset = true;
            //_timerEstadoOnline.Enabled = true;

            //Timer queconsulta tabla SIMPESOS
            _timerConsultaPesos = new System.Timers.Timer();
            _timerConsultaPesos.Elapsed += OnTimedEventConsultaPesos;
            _timerConsultaPesos.Interval = 1000;  //Ejecuto cada segundo
            _timerConsultaPesos.AutoReset = true;

            // Timer que actualiza el estado de los servicios para monitor cada 2 minutos
            _timerServicioMonitor = new System.Timers.Timer();
            _timerServicioMonitor.Elapsed += OnTimedEventServicioMonitor;
            _timerServicioMonitor.Interval = 2 * 60000;  //Ejecuto cada 2 minutos
            _timerServicioMonitor.AutoReset = true;
            _timerServicioMonitor.Enabled = true;

            // Timer que actualiza el estado de los servicios para monitor cada 2 minutos
            _timerServicioMonitor = new System.Timers.Timer();
            _timerServicioMonitor.Elapsed += OnTimedEventServicioMonitor;
            _timerServicioMonitor.Interval = 2 * 60000;  //Ejecuto cada 2 minutos
            _timerServicioMonitor.AutoReset = true;
            _timerServicioMonitor.Enabled = true;

            // Delegado para actualización de listas
            ModuloBaseDatos.Instance.ProcesarComando -= ProcesarComandoSupervision;
            ModuloBaseDatos.Instance.ProcesarComando += ProcesarComandoSupervision;
            ModuloBaseDatos.Instance.ResultadoConsultaBD -= ResultadoConsultaBD;
            ModuloBaseDatos.Instance.ResultadoConsultaBD += ResultadoConsultaBD;

            // Delegado recepción de datos Tarjeta Chip
            ModuloTarjetaChip.Instance.ProcesarLecturaTarjetaChip -= ProcesarTarjetaChip;
            ModuloTarjetaChip.Instance.ProcesarLecturaTarjetaChip += ProcesarTarjetaChip;

            // Delegado recepcion de datos Tarjeta de Credito
       ///     ModuloTarjetaCredito.Instance.ProcesarLecturaTarjetaCredito -= OnRespuestaServicioTC;
          //  ModuloTarjetaCredito.Instance.ProcesarLecturaTarjetaCredito += OnRespuestaServicioTC;

            // Para enviar al modulo de monitoreo
            ModuloAntena.Instance.ProcesarStatusComando += AntenaStatusComando;
            ModuloImpresora.Instance.ProcesarImpresora += ImpresoraStatusComando;
            ModuloFoto.Instance.ProcesarFoto += EventosFoto;
            ModuloVideo.Instance.ProcesarVideo += VideoStatusComando;
            ModuloDisplay.Instance.ProcesarDisplay += EventosDisplay;
            ModuloTarjetaChip.Instance.ProcesarStatus += EventosChip;

            _minutosPrevioCierreJornada = Convert.ToInt32(ClassUtiles.LeerConfiguracion("CAMBIO_JORNADA", "MINUTOSPREVIOCIERRE"));
            _minutosCierreModoMantenimiento = Convert.ToInt32(ClassUtiles.LeerConfiguracion("MODO_MANTENIMIENTO", "MINUTOSPARACIERRE"));
            string strAux = ClassUtiles.LeerConfiguracion("DATOS", "AlarmaMinimoEspacioLibreDiscoMB");
            _minimoEspacioLibreDiscoGB = Convert.ToInt32(!string.IsNullOrEmpty(strAux) ? strAux : "1024");

            ModuloVideoContinuo.Instance.AbrirCerrarVideoContinuo(eComandosVideoServer.Open);


            _seConfirmoNum = false;
            FecUltimoTransito = DateTime.Now;

            _init = false;

            _logger?.Info("Finaliza Constructor de LogicaCobroManual");
        }

        /// <summary>
        /// Chequea si viene de un reinicio y si es necesario reabrir turno
        /// de acuerdo a la configuracion
        /// </summary>
        private void ChequearUltimoEstado()
        {
            _logger?.Info("ChequearUltimoEstado -> Inicio");
            // Si el turno nunca se cerró, verifico si la configuracion me permite volver a abrirlo
            if (_turno.FechaCierre <= _turno.FechaApertura)
            {
                // Si abro siempre
                if (ModoPermite(ePermisosModos.AbrirAlReiniciar))
                {
                    _logger?.Info("ChequearUltimoEstado -> ePermisosModos.AbrirAlReiniciar [S]");
                    _modo = ModuloBaseDatos.Instance.BuscarModoXApertura(_turno.Modo);

                    if (_turno.Mantenimiento == 'S')
                        AperturaModoMantenimiento(_modo, false);
                    else if (_turno.ModoQuiebre == 'S')
                        InicioQuiebre(eOrigenComando.Automatica, false);
                    else if (_turno.Modo == "D")
                    {
                        Runner runner = new Runner();
                        runner.IDSupervisor = _turno.Operador.ID;
                        AperturaModoAutomatico(_modo, runner, eOrigenComando.Automatica, false);
                    }
                    else
                        AperturaTurno(_modo, _turno.Operador, false, eOrigenComando.Pantalla, false);

                    InicializarPantalla(true);

                }
                // Si puedo abrir luego de un cierto tiempo
                else
                {
                    _logger?.Info("ChequearUltimoEstado -> ePermisosModos.AbrirAlReiniciar [N]");
                    if (ModoPermite(ePermisosModos.AbrirAlReiniciarDespuesXTiempo))
                    {
                        _logger?.Info("ChequearUltimoEstado -> ePermisosModos.AbrirAlReiniciarDespuesXTiempo [S]");
                        ulong ultimoTimestamp = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.UltimoTimestamp);

                        DateTime ultimoTimeStamp = new DateTime((long)ultimoTimestamp);

                        TimeSpan timeSpan = Fecha - ultimoTimeStamp;

                        // Siendo 8 valor configurable
                        if (timeSpan.TotalHours < 8)
                        {
                            _modo = ModuloBaseDatos.Instance.BuscarModoXApertura(_turno.Modo);

                            if (_turno.Mantenimiento == 'S')
                                AperturaModoMantenimiento(_modo, false);
                            else if (_turno.ModoQuiebre == 'S')
                                InicioQuiebre(eOrigenComando.Automatica, false);
                            else if (_turno.Modo == "D")
                            {
                                Runner runner = new Runner();
                                runner.IDSupervisor = _turno.Operador.ID;
                                AperturaModoAutomatico(_modo, runner, eOrigenComando.Automatica, false);
                            }
                            else
                                AperturaTurno(_modo, _turno.Operador, false, eOrigenComando.Pantalla, false);

                            InicializarPantalla(true);
                        }
                        else
                        {
                            CierreTurno(false, eCodigoCierre.FinalTurno, eOrigenComando.Pantalla);
                        }
                    }
                    else
                    {
                        _logger?.Info("ChequearUltimoEstado -> ePermisosModos.AbrirAlReiniciarDespuesXTiempo [N]");
                        CierreTurno(false, eCodigoCierre.FinalTurno, eOrigenComando.Pantalla);
                    }
                }
            }
            _logger?.Info("ChequearUltimoEstado -> Fin");
        }

        /// <summary>
        /// Setea la logica de vía
        /// </summary>
        /// <param name="logicaVia"></param>
        override public void SetLogicaVia(ILogicaVia logicaVia)
        {
            _logicaVia = logicaVia;
        }

        /// <summary>
        ///  Setea estado de validacion (rockey o vencimiento) de la via
        /// </summary>
        /// <param name="estadoValidacionVia"></param>
        override public void SetEstadoValidacionVia(eEstadoValidacionVia estadoValidacionVia)
        {
            _estadoValidacionVia = estadoValidacionVia;

            if (_estadoValidacionVia == eEstadoValidacionVia.VersionViaDebug
             || _estadoValidacionVia == eEstadoValidacionVia.ViaConLlaveProteccionValida
             || _estadoValidacionVia == eEstadoValidacionVia.ViaConVencimientoNoCaducada)
            {
                _init = true;
            }
            else
            {
                _init = false;

                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir(ClassUtiles.GetEnumDescr(_estadoValidacionVia)));
            }
        }

        /// <summary>
        /// Carga todos los metodos correspondientes a ser llamados 
        /// por el delegado correspondiente en el modulo de pantalla
        /// </summary>
        public override void CargarDelegadosPantalla()
        {
            ModuloPantalla.Instance.CleanDelegates();
            ModuloPantalla.Instance.InitializeDelegates(TeclaAperturaCierreTurno,
                                                 ProcesarCredenciales,
                                                 ProcesarOpcionMenu,
                                                 ValidarCambioPassword,
                                                 TeclaMenu,
                                                 TeclaExento,
                                                 TeclaFoto,
                                                 TeclaCategoria,
                                                 TeclaPagoEfectivo,
                                                 TeclaPatente,
                                                 TeclaSimulacion,
                                                 TeclaCancelar,
                                                 TeclaDetracManual,
                                                 TeclaSubeBarrera,
                                                 TeclaBajaBarrera,
                                                 TeclaLiquidacion,
                                                 TeclaSemaforoMarquesina,
                                                 ProcesarPatenteIngresada,
                                                 TeclaTagManual,
                                                 TeclaFactura,
                                                 TeclaObservacion,
                                                 TeclaVideoInterno,
                                                 TeclaRetiro,
                                                 SalvarRetiroAnticipado,
                                                 TeclaMenuVenta,
                                                 TeclaUnViaje,
                                                 SalvarFondoDeCambio,
                                                 ValidarFactura,
                                                 SalvarLiquidacion,
                                                 SalvarExento,
                                                 SalvarTagManual,
                                                 CancelarTagPago,
                                                 ProcesarConfirmacion,
                                                 SalvarRecarga,
                                                 ConfirmarNumeracion,
                                                 ImprimirTotales,

                                                 null,
                                                 TeclaEscape,
                                                 ActualizarNumeracion,
                                                 RecibirComitiva,
                                                 RecibirComitiva,
                                                 ProcesarCategoEspecial,
                                                 ProcesarTeclaAlarma,
                                                 RecibirCobroDeuda,
                                                 RecibirEncuesta,
                                                 TeclaTarjetaCredito,
                                                 ProcesarFacturaTarjeta,
                                                 ProcesarTagTarjeta,
                                                 ProcesarVuelto,
                                                 GenerarPagoDiferido
                                                 );

            _logger?.Debug("LogicaCobroManual::CargarDelegadosPantalla -> Fin");
        }

        public override void InicializarPantalla(bool bIniciaSinMensajes)
        {
            if (bIniciaSinMensajes)
                ModuloPantalla.Instance.LimpiarTodo();

            // Actualizacion de mimicos en pantalla
            Mimicos mimicos = new Mimicos();

            mimicos.CampanaViolacion = enmEstadoAlarma.Ok;
            if (ModuloAntena.Instance.Socket == eEstadoSocket.Conectado)
                mimicos.EstadoAntena = enmEstadoDispositivo.OK;
            else
                mimicos.EstadoAntena = enmEstadoDispositivo.Error;
            if (ModuloImpresora.Instance.Socket == eEstadoSocket.Conectado)
            {
                ImprimiendoTicket = true;
                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);
                ImprimiendoTicket = false;
                if (errorImpresora == EnmErrorImpresora.SinFalla)
                    mimicos.EstadoImpresora = enmEstadoImpresora.OK;
            }
            else
                mimicos.EstadoImpresora = enmEstadoImpresora.Error;
            mimicos.EstadoRedLocal = enmEstadoDispositivo.OK;
            mimicos.EstadoRedServidor = enmEstadoDispositivo.OK;
            if (ModuloTarjetaChip.Instance.Socket == eEstadoSocket.Conectado)
                mimicos.EstadoTarjetaChip = enmEstadoDispositivo.OK;
            else
                mimicos.EstadoTarjetaChip = enmEstadoDispositivo.Error;

            // Envio de datos via a pantalla
            List<DatoVia> listaDatosVia = new List<DatoVia>();

            ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VIA, listaDatosVia);

            if (_estadoNumeracion == eEstadoNumeracion.NumeracionOk)
            {
                // Actualiza estado de turno en pantalla
                if (_turno.EstadoTurno == enmEstadoTurno.Abierta || _turno.EstadoTurno == enmEstadoTurno.Quiebre)
                {
                    ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Abierta") + ". " + Traduccion.Traducir("Modo") + ": " + _turno.ModoAperturaVia.ToString() + ". " + Traduccion.Traducir("Cajero") + ": " + _operadorActual.ID.ToString());
                    DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);
                    ModuloDisplay.Instance.Enviar(eDisplay.BNV);
                }
                else
                {
                    ClassUtiles.InsertarDatoVia(new Turno(), ref listaDatosVia);
                    //Apago semaforos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                    DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Rojo);
                    DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Desactivada);
                    ModuloDisplay.Instance.Enviar(eDisplay.CER);
                }
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);

                if (_turno.EstadoTurno == enmEstadoTurno.Abierta)
                {
                    // Actualiza estado de vehiculo en pantalla
                    Vehiculo vehAux = _logicaVia.GetPrimeroSegundoVehiculo();
                    Vehiculo vehiculo = new Vehiculo();
                    vehiculo.CopiarVehiculo(ref vehAux);
                    vehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                    vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
                    //Si es modo mantenimiento muestro ticket no fiscal
                    if (_turno.Mantenimiento == 'S')
                        vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketNoFiscal);
                    else
                    {
                        vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                        vehiculo.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
                    }
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);
                    ModuloDisplay.Instance.Enviar(eDisplay.BNV);

                    //se limpia numero de ticket
                    vehiculo.NumeroTransito = 0;
                    vehiculo.NumeroTicketF = 0;
                    vehiculo.NumeroTicketNF = 0;
                    vehiculo.NumeroDetraccion = 0;


                }
                else
                {
                    //Envio a pantalla el ultimo numero de transito y factura
                    Vehiculo veh = new Vehiculo();
                    veh.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                    veh.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                    veh.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
                    veh.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
                    veh.FormaPago = eFormaPago.Nada;
                    ModuloPantalla.Instance.LimpiarVehiculo(veh);
                    //Apago semaforos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                    DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Rojo);
                    DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Desactivada);
                    ModuloDisplay.Instance.Enviar(eDisplay.CER);
                }

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
            }
            else if (_estadoNumeracion == eEstadoNumeracion.NumeracionSinConfirmar)
            {
                //Envio a pantalla el ultimo numero de transito y factura
                Vehiculo veh = new Vehiculo();
                veh.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                veh.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                veh.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
                veh.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
                veh.FormaPago = eFormaPago.Nada;
                ModuloPantalla.Instance.LimpiarVehiculo(veh);
            }

            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            _logger?.Debug("Pantalla inicializada");
        }

        #endregion

        #region TimerDispose
        public override void Dispose()
        {
            _timerFecha?.Stop();
            _timerFecha?.Dispose();

            _timerChequeo?.Stop();
            _timerChequeo?.Dispose();

            _timerCambioTarifa?.Stop();
            _timerCambioTarifa?.Dispose();

            _timerControlCambioTarifa?.Stop();
            _timerControlCambioTarifa?.Dispose();

            _timerConsultaParte?.Stop();
            _timerConsultaParte?.Dispose();

            _timerSeteoDAC?.Stop();
            _timerSeteoDAC?.Dispose();

            _timerEstadoImp?.Stop();
            _timerEstadoImp?.Dispose();

            _timerEstadoOnline?.Stop();
            _timerEstadoOnline?.Dispose();

            _timerCierreModoMantenimiento?.Stop();
            _timerCierreModoMantenimiento?.Dispose();

            _timerConsultaPesos?.Stop();
            _timerConsultaPesos?.Dispose();

            _logger.Debug("Dispose -> Dispose Timers");
        }
        #endregion

        #region Apertura de Turno

        /// <summary>
        /// Procesa la apertura de turno, se utiliza para distintos momentos (Tecla presionada, seleccion de modo, y confirmacion de apertura)
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="modo"></param>
        /// <param name="origenComando"></param>
        /// <param name="operador"></param>
        /// <param name="causaAp"></param>
        private async Task ProcesarAperturaTurno(eTipoComando tipoComando, Modos modo, eOrigenComando origenComando, Operador operador, eCausas causaAp)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (!AbriendoTurno)
                AbriendoTurno = true;

            if (!ModuloImpresora.GetEstadoXML())
            {
                ModuloImpresora.ShowXMLError();
            }

            else if (ValidarPrecondicionesAperturaTurno(origenComando, causaAp))
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    Causa causa = new Causa(causaAp, ClassUtiles.GetEnumDescr(causaAp));

                    // Si hay mas de un modo de apertura manual, no solicito confirmar login
                    Opcion opcionApertura = new Opcion();
                    List<Modos> listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);
                    if (listaModos?.Count(x => x.Cajero == "S") > 0)
                    {
                        opcionApertura.Confirmar = false;
                    }
                    else
                    {
                        opcionApertura.Confirmar = true;
                    }

                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(opcionApertura, ref listaDatosVia);
                    _logger.Debug("Se envían modos de apertura. Confirmar [{0}]", opcionApertura.Confirmar ? "S" : "N");
                    // Puede abrir turno - Se solicita usuario y contraseña
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.LOGIN, listaDatosVia);
                }
                // Confirmacion de credenciales
                else if (tipoComando == eTipoComando.eConfirmacion)
                {
                    // Apertura
                    if (causaAp == eCausas.AperturaTurno)
                    {
                        if (!IsNumeracionOk() && ModoPermite(ePermisosModos.AutorizarNumeracion))
                        {
                            int nivelAcceso;
                            if (int.TryParse(operador.NivelAcceso, out nivelAcceso))
                            {
                                if (nivelAcceso != (int)eNivelUsuario.Tecnico)
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VerificarNumeracion);
                                    ModuloPantalla.Instance.EnviarDatos(enmStatus.FallaCritica, enmAccion.ESTADO_SUB, null);
                                }
                                else
                                    await VerificarAperturaVia(null, origenComando, operador);
                            }
                            else
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.SinNivel);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                                _logger?.Warn("ProcesarAperturaTurno -> Error al intentar castear el nivel de acceso");
                            }
                        }
                        else
                        {
                            // Aun no tengo el modo de apertura, por eso se manda en null
                            _logger?.Debug("ProcesarAperturaTurno -> Aun no tengo el modo de apertura. NumeracionOk [{0}] AutorizarNumeracion [{1}]",
                                            IsNumeracionOk() ? "TRUE" : "FALSE", ModoPermite(ePermisosModos.AutorizarNumeracion) ? "TRUE" : "FALSE");
                            await VerificarAperturaVia(null, origenComando, operador);
                        }
                    }
                }
                // Seleccion de modo
                else if (tipoComando == eTipoComando.eSeleccion)
                {
                    await VerificarAperturaVia(modo, origenComando, operador);
                }
            }

            if (AbriendoTurno)
                AbriendoTurno = false;
        }

        /// <summary>
        /// Valida condiciones necesarias para que se pueda abrir el turno
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesAperturaTurno(eOrigenComando origenComando, eCausas causa = eCausas.AperturaTurno)
        {
            bool retValue = true;

            if (!_init)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": " + eMensajesPantalla.ViaNoInicializada.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaNoInicializada.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);

                retValue = false;
            }
            else if (_estado == eEstadoVia.EVAbiertaLibre && causa != eCausas.Quiebre)
            {
                if (!ModoPermite(ePermisosModos.AperturaTurno))
                {
                    if (origenComando == eOrigenComando.Supervision)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": " + eMensajesPantalla.ViaYaAbierta.GetDescription());
                        ResponderComandoSupervision("X", eMensajesPantalla.ViaYaAbierta.GetDescription());
                    }
                    else
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaYaAbierta);

                    retValue = false;
                }
            }
            else if (DAC_PlacaIO.Instance.EntradaBidi())
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": " + eMensajesPantalla.ViaOpuestaAbierta.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaOpuestaAbierta.GetDescription());
                    retValue = false;
                }
            }
            else if (SinEspacioEnDisco())
                retValue = false;

            return retValue;
        }

        /// <summary>
        /// De acuerdo al modo y el nivel de usuario, 
        /// procede a abrir turno o envia a pantalla opciones de apertura
        /// </summary>
        /// <param name="modo"></param>
        /// <param name="origenComando"></param>
        /// <param name="operador"></param>
        private async Task VerificarAperturaVia(Modos modo, eOrigenComando origenComando, Operador operador)
        {
            int nivelAcceso = 0;

            if (!int.TryParse(operador.NivelAcceso, out nivelAcceso))
            {
                _logger?.Info("VerificarAperturaVia -> Error al intentar castear el nivel de acceso");

                if (AbriendoTurno)
                    AbriendoTurno = false;

                if (origenComando == eOrigenComando.Supervision)
                {
                    _logger?.Info("VerificarAperturaVia -> eOrigenComando.Supervision");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": " + "Formato de Operador erróneo");
                    ResponderComandoSupervision("X", "Formato de Operador erroneo");
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.FallaCritica, enmAccion.ESTADO_SUB, null);
                }
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
            }
            else
            {
                _logger?.Debug("VerificarAperturaVia -> Operador [{0}] NivelAcceso [{1}]", operador.ID, operador.NivelAcceso);

                if (DAC_PlacaIO.Instance.EntradaBidi() && nivelAcceso != (int)eNivelUsuario.Tecnico)
                {
                    ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.AbrirVia);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaOpuestaAbierta);
                    return;
                }

                // Supervisor
                if (nivelAcceso == (int)eNivelUsuario.Supervisor)
                {
                    if (origenComando != eOrigenComando.Supervision)
                    {
                        ListadoOpciones listaOpciones = new ListadoOpciones();

                        // Carga el menu con las opciones correspondientes
                        int orden = 1;

                        CargarOpcionMenu(ref listaOpciones, eAperturasSupervisor.AbrirVia.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eAperturasSupervisor.AbrirVia), string.Empty, orden++);

                        //if( ModoPermite( ePermisosModos.ImprimirTotales ) )
                        {
                            CargarOpcionMenu(ref listaOpciones, eAperturasSupervisor.ImpresionTotales.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eAperturasSupervisor.ImpresionTotales), string.Empty, orden++);

                            CargarOpcionMenu(ref listaOpciones, eAperturasSupervisor.ImpresionTotalesOtroTurno.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eAperturasSupervisor.ImpresionTotalesOtroTurno), string.Empty, orden++);
                        }

                        // Genero la causa de apertura
                        Causa causaApertura = new Causa();
                        causaApertura.Codigo = eCausas.OpcionesSupervisor;
                        causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.OpcionesSupervisor);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaOpciones, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(causaApertura, ref listaDatosVia);

                        // Se envia lista de causas posibles de apertura para Supervisor
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    else
                    {
                        await AperturaTurno(modo, operador, false, origenComando, true, true);
                    }
                }
                // Tecnico
                else if (nivelAcceso == (int)eNivelUsuario.Tecnico ||
                         nivelAcceso == (int)eNivelUsuario.Sistemas)
                {
                    if (origenComando != eOrigenComando.Supervision)
                    {
                        ListadoOpciones listaOpciones = new ListadoOpciones();

                        // Carga el menu con las opciones correspondientes
                        int orden = 1;

                        if (nivelAcceso == (int)eNivelUsuario.Sistemas)
                            CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.ModoMantenimiento.ToString(), true,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.ModoMantenimiento), string.Empty, orden++);

                        CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.Testeo.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eOpcionesTecnico.Testeo), string.Empty, orden++);
                        CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.Reinicio.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eOpcionesTecnico.Reinicio), string.Empty, orden++);
                        CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.CierreSesion.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eOpcionesTecnico.CierreSesion), string.Empty, orden++);
                        CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.Apagado.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eOpcionesTecnico.Apagado), string.Empty, orden++);
                        CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.Versiones.ToString(), true,
                                          ClassUtiles.GetEnumDescr(eOpcionesTecnico.Versiones), string.Empty, orden++);

                        if (_numeracion.InformacionTurno == null)
                        {
                            if (_estadoNumeracion == eEstadoNumeracion.NumeracionOk)
                                CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.ConsultarNumeracion.ToString(), false,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.ConsultarNumeracion), string.Empty, orden++);
                            else
                                CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.VerificarNumeracion.ToString(), false,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.VerificarNumeracion), string.Empty, orden++);
                        }
                        else
                        {
                            if (_estadoNumeracion == eEstadoNumeracion.NumeracionSinConfirmar && ModoPermite(ePermisosModos.AutorizarNumeracion))
                                CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.ConfirmarNumeracion.ToString(), false,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.ConfirmarNumeracion), string.Empty, orden++);
                            if (_estadoNumeracion == eEstadoNumeracion.NumeracionOk)
                                CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.ConsultarNumeracion.ToString(), false,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.ConsultarNumeracion), string.Empty, orden++);
                            else
                                CargarOpcionMenu(ref listaOpciones, eOpcionesTecnico.VerificarNumeracion.ToString(), false,
                                              ClassUtiles.GetEnumDescr(eOpcionesTecnico.VerificarNumeracion), string.Empty, orden++);
                        }

                        // Genero la causa de apertura
                        Causa causaApertura = new Causa();
                        causaApertura.Codigo = eCausas.OpcionesTecnico;
                        causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.OpcionesTecnico);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaOpciones, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(causaApertura, ref listaDatosVia);

                        // Se envia lista de causas posibles de apertura para Tecnico
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    else
                    {
                        await AperturaTurno(modo, operador, false, origenComando, true, true);
                    }
                }
                // Cajero
                else if (nivelAcceso == (int)eNivelUsuario.Cajero)
                {
                    if (origenComando != eOrigenComando.Supervision)
                    {
                        await EnviaModos(modo, operador, origenComando, eNivelUsuario.Cajero);
                    }
                    else
                    {
                        await AperturaTurno(modo, operador, false, origenComando, true, true);
                    }
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NivelNoPermitido);

                    if (AbriendoTurno)
                        AbriendoTurno = false;

                    if (origenComando == eOrigenComando.Supervision)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": " + ClassUtiles.GetEnumDescr(eMensajesPantalla.NivelNoPermitido));
                        ResponderComandoSupervision("X", ClassUtiles.GetEnumDescr(eMensajesPantalla.NivelNoPermitido));
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.FallaCritica, enmAccion.ESTADO_SUB, null);
                    }
                    else
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }
        }

        private async Task EnviaModos(Modos modo, Operador operador, eOrigenComando origenComando, eNivelUsuario nivelUsuario)
        {
            List<Modos> listaModos = null;

            // Si todavía no tengo el modo, busco los posibles modos de apertura
            if (modo == null)
                listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);

            // Si tengo más un posible modo(con cajero) de apertura 
            // y no recibi el modo seleccionado de pantalla
            if (listaModos?.Count(x => x.Cajero == "S") > 0 && modo == null)
            {
                ListadoOpciones opciones = new ListadoOpciones();

                int orden = 1;

                // Para cada modo posible de apertura con cajero, genero una opcion a mostrar en pantalla
                foreach (Modos modoAux in listaModos.Where(x => x.Cajero == "S"))
                {
                    CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(modoAux), true, modoAux.Descripcion, string.Empty, orden++, false);
                }

                // Genero la causa de apertura
                Causa causaApertura = new Causa();

                switch (nivelUsuario)
                {
                    case eNivelUsuario.Cajero:
                        causaApertura.Codigo = eCausas.AperturaTurno;
                        causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.AperturaTurno);
                        break;

                    case eNivelUsuario.Supervisor:
                        causaApertura.Codigo = eCausas.AperturaTurnoSupervisor;
                        causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.AperturaTurnoSupervisor);
                        break;

                    case eNivelUsuario.Tecnico:
                        causaApertura.Codigo = eCausas.AperturaTurnoMantenimiento;
                        causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.AperturaTurnoMantenimiento);
                        break;
                    default:
                        break;
                }

                opciones.MuestraOpcionIndividual = false; //Si se envia una sola opcion en el listado, se muestra de todas formas

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(causaApertura, ref listaDatosVia);

                // Se envia lista de causas posibles de apertura para Cajero
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
            }
            else
            {
                // Asigno los datos del cajero a _turno.Parte, 
                // para que despues la pantalla pueda obtenerlos
                if (operador == null)
                    operador = new Operador();

                _turno.Parte.IDCajero = operador.ID;
                _turno.Parte.NombreCajero = operador.Nombre;
                _turno.Operador = operador;

                _turno.OrigenApertura = 'M';
                _turno.PcSupervision = (origenComando == eOrigenComando.Supervision) ? 'S' : 'N';

                // No es necesario enviar a pantalla la lista de modos 
                // para apertura porque solo hay uno
                if (listaModos != null && modo == null && listaModos.Any())
                {
                    // Obtiene el modo "Sin Cajero"
                    modo = listaModos[listaModos.FindIndex(x => x.Cajero == "S")];
                }
                //else if( modo == null )
                //{
                //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("No existen modos de apertura" ));
                //}

                if (modo != null)
                {
                    // Abrimos turno
                    await AperturaTurno(modo, operador, false, origenComando, true, true);
                }

                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("E");
            }
        }

        private void IniciarTimerSeteoDAC()
        {
            // Inicializacion del timer
            if (!_timerSeteoDAC.Enabled)
            {
                _timerSeteoDAC.Elapsed -= new ElapsedEventHandler(OnTimerSetearDAC);
                _timerSeteoDAC.Elapsed += new ElapsedEventHandler(OnTimerSetearDAC);
                _timerSeteoDAC.Interval = 1000;
                _timerSeteoDAC.AutoReset = true;
                _timerSeteoDAC.Enabled = true;
                OnTimerSetearDAC(null, null);
            }
        }

        private bool m_bDacFail = false;
        private void OnTimerSetearDAC(object source, ElapsedEventArgs e)
        {
            byte PICErr = 0, bEstado = 0;
            short ret;
            ret = DAC_PlacaIO.Instance.ObtenerFallaSensores(ref PICErr, ref bEstado);

            if (ret >= 0)
            {
                ret = DAC_PlacaIO.Instance.ModoPeanas(ModuloBaseDatos.Instance.ConfigVia.ContadorEjes != 0);

                if (ret >= 0)
                {
                    if (m_bDacFail)
                    {
                        FallaCritica oFallaCritica = new FallaCritica();
                        oFallaCritica.CodFallaCritica = EnmFallaCritica.FCPic;
                        oFallaCritica.Observacion = "Se Logró Comunicación con el DAC";
                        ModuloEventos.Instance.SetFallasCriticas(GetTurno, oFallaCritica, null);
                        m_bDacFail = false;
                    }

                    //No hubo fallo, detengo el timer
                    _timerSeteoDAC.Enabled = false;
                    //Seteo la configuración en el pic segun el modo de apertura
                    if (_turno.EstadoTurno == enmEstadoTurno.Quiebre)
                        DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.Quiebre);
                    else
                    {
                        if (_turno.ModoAperturaVia == enmModoAperturaVia.Manual)
                            DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.Manual);
                        else if (_turno.ModoAperturaVia == enmModoAperturaVia.Dinamico || _turno.ModoAperturaVia == enmModoAperturaVia.MD)
                            DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.Dinamico);
                        else if (_turno.ModoAperturaVia == enmModoAperturaVia.AVI)
                            DAC_PlacaIO.Instance.EstablecerModelo(ModosDAC.AVI);
                    }
                }
            }
            else
            {
                if (!m_bDacFail)
                {
                    m_bDacFail = true;
                    FallaCritica oFallaCritica = new FallaCritica();
                    oFallaCritica.CodFallaCritica = EnmFallaCritica.FCPic;
                    oFallaCritica.Observacion = "Fallo de Comunicación con DAC";
                    ModuloEventos.Instance.SetFallasCriticas(GetTurno, oFallaCritica, null);
                }
            }
        }

        /// <summary>
        /// Se realiza la apertura de turno, cambiando de estado los perifericos correspondientes
        /// y enviando el evento de apertura a la base
        /// </summary>
        /// <param name="modo"></param>
        public override async Task AperturaTurno(Modos modo, Operador operador, bool aperturaAutomatica, eOrigenComando origenComando, bool EnviarSetApertura = true, bool confirmar = false)
        {
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            Causa causa = new Causa();

            if (confirmar && origenComando == eOrigenComando.Pantalla)
            {
                //    causa = new Causa( eCausas.AperturaTurno, eCausas.AperturaTurno.GetDescription() );

                //ClassUtiles.InsertarDatoVia( causa, ref listaDatosVia );
                //ClassUtiles.InsertarDatoVia( modo, ref listaDatosVia );
                //ClassUtiles.InsertarDatoVia( operador, ref listaDatosVia );

                //ModuloPantalla.Instance.EnviarDatos( enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia );
                await AperturaTurno(modo, operador, false, eOrigenComando.Pantalla);

                return;
            }

            if (_ultimaAccion.MismaAccion(enmAccion.T_TURNO))
                _ultimaAccion.Clear();

            _estado = eEstadoVia.EVAbiertaLibre;

            SetOperadorActual(operador);
            SetModo(modo);
            AsignarModoATurno();

            // Actualiza las variables necesarios de Turno
            if (origenComando == eOrigenComando.Pantalla)
                _turno.OrigenApertura = 'M';
            else if (origenComando == eOrigenComando.Supervision)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": OK");
                _turno.OrigenApertura = 'S';
            }
            else
                _turno.OrigenApertura = 'R';

            _turno.Modo = modo.Modo;
            _turno.Mantenimiento = 'N';
            _turno.EstadoTurno = enmEstadoTurno.Abierta;
            _turno.FechaApertura = EnviarSetApertura ? DateTime.Now : _turno.FechaApertura;
            ulong auxLong = await ModuloBaseDatos.Instance.ObtenerNumeroTurnoAsync();
            if (EnviarSetApertura)
                _turno.NumeroTurno = auxLong == 0 ? ++_turno.NumeroTurno : auxLong; //si esta cerrada y no responde a tiempo incremento el nro de turno
            else
                _turno.NumeroTurno = auxLong == 0 ? _turno.NumeroTurno : auxLong;
            _turno.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido == 'A' ? enmSentido.Ascendente : enmSentido.Descendente;
            _turno.Ticket = (int)ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
            _turno.Factura = (int)ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
            _turno.ModoQuiebre = 'N';

            // Limpio de pantalla los mensajes de lineas
            if (!aperturaAutomatica)
                ModuloPantalla.Instance.LimpiarMensajes();

            //Inicio el timer de chequeo de cambio de tarifa
            _timerControlCambioTarifa.Start();

            //Consulto el parte
            bool bParte = await ConsultarParte(origenComando == eOrigenComando.Automatica ? _runnerActual : null);

            //no se pudo consultar el parte, limpiamos datos del parte
            if (!bParte)
            {
                ParteBD parteBD = new ParteBD();
                _turno.Parte.Fondo = parteBD.Fondo.GetValueOrDefault();
                _turno.Parte.JornadaContable = parteBD.JornadaContable.GetValueOrDefault();
                _turno.Parte.MontoFondoCaja = parteBD.MontoFondoCambio.GetValueOrDefault();
                _turno.Parte.NumeroParte = parteBD.Numero.GetValueOrDefault();
            }

            _turno.Parte.NombreCajero = _operadorActual.Nombre;
            _turno.Parte.IDCajero = _operadorActual.ID;

            ulong numeroTarjeta;
            ulong.TryParse(_operadorActual.ID, out numeroTarjeta);

            _turno.NumeroTarjeta = numeroTarjeta;
            _turno.Operador = operador;

            listaDatosVia.Clear();
            listaDatosVia = new List<DatoVia>();
            // Obtiene el vehiculo correspondiente
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            // Si fuera apertura automatica no debe actualizar los perifericos
            if (!aperturaAutomatica)
            {
                //Limpio el vehiculo por si quedo algo anterior
                //vehiculo.Clear(true);

                //Imprimo el encabezado
                await ImprimirEncabezado(false);

                IniciarTimerSeteoDAC();

                // Se actualiza el estado de los perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);
                DAC_PlacaIO.Instance.AccionCartelMarquesina(modo.Modo);
                DAC_PlacaIO.Instance.AccionSalidaModo(modo.Modo);

                if (ModoPermite(ePermisosModos.CerrarBarreraAlAbrir))
                {
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                    _logger?.Debug("AperturaTurno -> BARRERA ABAJO!! [{0}]", ePermisosModos.CerrarBarreraAlAbrir.ToString());
                }

                if (ModoPermite(ePermisosModos.SemMarquesinaVerdeAlAbrir))
                    DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Verde);

                ModuloDisplay.Instance.Enviar(eDisplay.BNV);
                ModuloAntena.Instance.ActivarAntena();
            }

            // Actualiza estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            // Actualiza estado de turno en pantalla
            listaDatosVia.Clear();
            listaDatosVia = new List<DatoVia>();

            ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

            // Actualiza estado de vehiculo en pantalla
            listaDatosVia = new List<DatoVia>();
            vehiculo.NumeroTicketF = (ulong)_turno.Ticket;
            vehiculo.NumeroFactura = (ulong)_turno.Factura;
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
            vehiculo.NumeroTicketF = 0; //se limpia numero de ticket
            vehiculo.NumeroFactura = 0;

            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.AbrirVia);

            //Para limpiar el flag de sentido opuesto, sino la vía no genera violaciones.
            _logicaVia.UltimoSentidoEsOpuesto = false;

            _turno.PcSupervision = 'N';

            if (origenComando == eOrigenComando.Supervision)
                _turno.PcSupervision = 'S';

            // Se almacena turno en la BD local y Se envia el evento de apertura de bloque
            if (EnviarSetApertura)
            {
                await ModuloBaseDatos.Instance.AlmacenarTurnoAsync(_turno);
                ModuloEventos.Instance.SetAperturaBloque(ModuloBaseDatos.Instance.ConfigVia, _turno);
            }

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.AperturaVia, ModuloBaseDatos.Instance.ConfigVia, _turno, null);

            _turno.PcSupervision = 'N';

            // Resetear contadores
            // Se encarga el modulo de base de datos

            // Manda el comando ESTADO a pantalla para cerrar las subventanas correspondientes
            listaDatosVia.Clear();
            listaDatosVia = new List<DatoVia>();
            causa = new Causa(eCausas.AperturaTurno, eCausas.AperturaTurno.ToString());
            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO, listaDatosVia);

            //if( ModeloPermite( ePermisosModelos.Autotabular ) )
            //    Categorizar((short)ModuloBaseDatos.Instance.ConfigVia.MonoCategoAutotab);

            ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

            //Borrar lista de tags del servicio de antena
            ModuloAntena.Instance.BorrarListaTags();

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            if (!aperturaAutomatica)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Abierta"));
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Modo") + ": " + _turno.ModoAperturaVia.ToString() + " - " + Traduccion.Traducir("Cajero") + ": " + _operadorActual.ID.ToString());
            }

            if (AbriendoTurno)
                AbriendoTurno = false;

            decimal vuelto = 0;
            int monto = 0;
            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vuelto, ref listaDatosVia2);
            ClassUtiles.InsertarDatoVia(monto, ref listaDatosVia2);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia2);

            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);

            _logger?.Info("Apertura de Turno realizada. Modo: {0}. Operador: {1} AperturaAutomatica: {2}", _turno?.ModoAperturaVia.ToString(), _operadorActual?.ID, aperturaAutomatica ? "S" : "N");
            _loggerTransitos?.Info($"A;{DateTime.Now.ToString("HH:mm:ss.ff")};{_operadorActual?.ID};{_turno?.ModoAperturaVia};{_turno?.Mantenimiento};{_turno?.ModoQuiebre}");
        }

        private void InicioTimerConsultaParte()
        {
            if (!_timerConsultaParte.Enabled)
            {
                // Inicializacion del timer
                _timerConsultaParte.Elapsed -= new ElapsedEventHandler(OnTimerConsultarParte);
                _timerConsultaParte.Elapsed += new ElapsedEventHandler(OnTimerConsultarParte);
                _timerConsultaParte.Interval = 1000;
                _timerConsultaParte.AutoReset = true;
                _timerConsultaParte.Enabled = true;
            }
        }

        private async void OnTimerConsultarParte(object source, ElapsedEventArgs e)
        {
            try
            {
                await ConsultarParte();
            }
            catch
            {

            }
        }

        private async Task<bool> ConsultarParte(Runner runner = null)
        {
            bool nRet = false;

            DateTime fechaParte;

            if (runner != null)
            {
                TimeSpan timeSpan = Fecha - runner.FechaInicioTurno;

                if (ModoPermite(ePermisosModos.RunnerObsoleto) && timeSpan.TotalHours >= 24)
                    fechaParte = Fecha;
                else
                    fechaParte = runner.FechaInicioTurno;
            }
            else
                fechaParte = _turno.FechaApertura == DateTime.MinValue ? DateTime.Now : _turno.FechaApertura;

            // Consulta de Parte
            ParteBD parteBD = await ModuloBaseDatos.Instance.ObtenerParteAsync(_operadorActual?.ID, fechaParte, _turno.Modo);

            // Si se pudo obtener un parte
            if (parteBD.TurnoTrabajo > 0 && parteBD.JornadaContable != DateTime.MinValue)
            {
                nRet = true;

                if (_timerConsultaParte.Enabled)
                {
                    _timerConsultaParte.Elapsed -= new ElapsedEventHandler(OnTimerConsultarParte);
                    _timerConsultaParte.Enabled = false;
                }

                if (_turno.Parte == null)
                    _turno.Parte = new Parte();

                _turno.Parte.Fondo = parteBD.Fondo.GetValueOrDefault();
                _turno.Parte.JornadaContable = parteBD.JornadaContable.GetValueOrDefault();
                _turno.Parte.MontoFondoCaja = parteBD.MontoFondoCambio.GetValueOrDefault();
                _turno.Parte.NumeroParte = parteBD.Numero.GetValueOrDefault();

                _turno.Parte.IDCajero = _operadorActual?.ID;
                _turno.Parte.NombreCajero = _operadorActual?.Nombre;
                _turno.Operador = _operadorActual;

                // Actualiza estado de turno en pantalla
                List<DatoVia> listaDatosVia = new List<DatoVia>();

                ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

                await ModuloBaseDatos.Instance.ActualizarNumeroParteTurnoAsync(_turno.Parte.NumeroParte);

                _logger?.Debug("Se obtuvo el parte: {0}", _turno.Parte.NumeroParte);
            }
            else
            {
                // Inicia un timer que intenta consultar el parte hasta que lo obtiene
                InicioTimerConsultaParte();
            }

            return nRet;
        }

        #endregion

        #region Cierre de Turno

        /// <summary>
        /// Procesa el cierre de turno, se utiliza para distintos momentos (Tecla presionada y seleccion de causa de cierre)
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <param name="causa"></param>
        private async Task ProcesarCierreTurno(eTipoComando tipoComando, eOrigenComando origenComando, CausaCierre causa)
        {
            if (_turno.ModoQuiebre == 'S')
                await ProcesarFinQuiebre(eOrigenComando.Pantalla);
            else
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloTarjetaChip.Instance.FinalizaLectura();

                if (!CerrandoTurno)
                    CerrandoTurno = true;

                if (ValidarPrecondicionesCierreTurno(tipoComando, origenComando))
                {
                    if (tipoComando == eTipoComando.eTecla)
                    {
                        List<CausaCierre> listaCausasCierre = await ModuloBaseDatos.Instance.BuscarCausasCierreAsync();

                        // Si tengo al menos una causa de cierre
                        if (listaCausasCierre?.Count > 0)
                        {
                            ListadoOpciones opciones = new ListadoOpciones();

                            Causa causaCierre = new Causa();
                            causaCierre.Codigo = eCausas.CausaCierre;
                            causaCierre.Descripcion = ClassUtiles.GetEnumDescr(eCausas.CausaCierre);

                            // Carga el menu de causas a mostrar en pantalla
                            foreach (CausaCierre causasCierre in listaCausasCierre.Where(x => x.EnVia == "S"))
                            {
                                CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causasCierre), true,
                                                  causasCierre.Descripcion, string.Empty, causasCierre.Orden, false);
                            }

                            opciones.MuestraOpcionIndividual = false;

                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(causaCierre, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                            // Envio la lista de causas de cierre a pantalla
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                        }
                        //else
                        //{
                        //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de cierre" ));
                        //}
                    }
                    // Se seleccionó una causa de cierre
                    else if (tipoComando == eTipoComando.eSeleccion)
                    {
                        await VerificarCausaCierre(causa, origenComando);
                    }
                }

                if (CerrandoTurno)
                    CerrandoTurno = false;

            }
        }

        /// <summary>
        /// Valida condiciones necesarias para que se pueda cerrar el turno
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesCierreTurno(eTipoComando tipoComando, eOrigenComando origenComando)
        {
            bool retValue = true;

            bool bVehiculosPagados = _logicaVia.GetHayVehiculosPagados();

            if (_estado == eEstadoVia.EVCerrada)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cerrar Vía") + ": " + eMensajesPantalla.ViaYaCerrada.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaYaCerrada.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaYaCerrada);

                retValue = false;
            }
            else if (bVehiculosPagados)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cerrar Vía") + ": " + eMensajesPantalla.ExistenVehiculosPagados.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ExistenVehiculosPagados.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ExistenVehiculosPagados);

                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.CierreTurnoBarreraAbierta) && DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cerrar Vía") + ": " + eMensajesPantalla.BarreraAbierta.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.BarreraAbierta.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BarreraAbierta);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// De acuerdo a la causa de cierre se seleccionada se procede a 
        /// por ejemplo Cerrar Turno o a realizar una Liquidacion
        /// </summary>
        /// <param name="respuesta"></param>
        /// <param name="origen"></param>
        private async Task VerificarCausaCierre(CausaCierre causa, eOrigenComando origen)
        {
            int causaCierre = 0;

            if (!int.TryParse(causa.Codigo, out causaCierre))
            {
                _logger?.Warn("Error al parsear causa cierre");

                if (origen == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", "Error de formato en la causa de cierre");

                if (CerrandoTurno)
                    CerrandoTurno = false;
            }
            else
            {
                if (causa.ConLiquidacion == "S")
                    await ProcesarLiquidacion(null);
                else
                    await CierreTurno(false, (eCodigoCierre)causaCierre, origen);
            }
        }

        /// <summary>
        /// Se realiza el cierre de turno, cambiando de estado los perifericos (si corresponde)
        /// y enviando el evento de cierre
        /// </summary>
        public override async Task CierreTurno(bool cierreAutomatico, eCodigoCierre codCierre, eOrigenComando origenComando)
        {
            _estado = eEstadoVia.EVCerrada;
            _turno.EstadoTurno = enmEstadoTurno.Cerrada;
            _turno.FechaCierre = DateTime.Now;
            _turno.CausaCierre = codCierre;
            _turno.Mantenimiento = _esModoMantenimiento ? 'S' : 'N';

            if (_esModoMantenimiento)
            {
                _timerCierreModoMantenimiento?.Stop();
            }

            //Detengo el timer de chequeo de cambio de tarifa
            _timerControlCambioTarifa.Stop();

            if (_timerCambioJornada.Enabled)
                _timerCambioJornada.Stop();

            List<DatoVia> listaDatosVia = new List<DatoVia>();

            if (origenComando == eOrigenComando.Supervision)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cerrar Vía") + ": OK");

            // Si fuera cierre automatico no debo modificar el estado de los perifericos
            if (!cierreAutomatico)
            {
                // Se actualiza el estado de los perifericos
                if (ModoPermite(ePermisosModos.CerrarBarreraAlAbrir))
                {
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                    _logger?.Debug("CierreTurnoTurno -> BARRERA ABAJO!! [{0}]", ePermisosModos.CerrarBarreraAlAbrir.ToString());
                }
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Rojo);
                DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Desactivada);
                DAC_PlacaIO.Instance.AccionCartelMarquesina("");

                ModuloPantalla.Instance.LimpiarVehiculo(new Vehiculo());

                //Limpio los tags leidos, para poder volver a leer el mismo tag
                _logicaVia.LimpiarTagsLeidos();
                //Limpio la cola de vehiculos
                //_logicaVia.LimpiarColaVehiculos(); //GAB: comentamos porque no está bien que borre la detección y el cobro de todo, mejorar

                if (_timerConsultaParte.Enabled)
                {
                    _timerConsultaParte.Elapsed -= new ElapsedEventHandler(OnTimerConsultarParte);
                    _timerConsultaParte.Enabled = false;
                }

                // Envia mensaje al display
                ModuloDisplay.Instance.Enviar(eDisplay.CER);

                ModuloAntena.Instance.DesactivarAntena();

                // Limpio los mensajes de las lineas
                ModuloPantalla.Instance.LimpiarMensajes();
            }
            // Actualiza estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            // Limpia la pantalla al cerrar el turno
            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.CerrarVia);

            // Limpia posible alarma de apertura en modo mantenimiento
            ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.ViaAbiertaMantenimiento, 0);

            // Se envia el evento de cierre de bloque
            TotalesTurno totalesTurno = ModuloBaseDatos.Instance.ObtenerTotalesTurno(_turno.FechaCierre);

            _turno.PcSupervision = 'N';

            if (origenComando == eOrigenComando.Supervision)
                _turno.PcSupervision = 'S';

            ModuloEventos.Instance.SetCierreBloqueXML(ModuloBaseDatos.Instance.ConfigVia, _turno, totalesTurno);

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.CierreVia, ModuloBaseDatos.Instance.ConfigVia, _turno, null);

            if (!cierreAutomatico)
            {
                Turno turnoAux = ClassUtiles.Clonar(_turno);
                listaDatosVia = new List<DatoVia>();
                turnoAux.Operador = new Operador();
                turnoAux.Parte = new Parte();

                // Actualiza estado del turno en pantalla
                ClassUtiles.InsertarDatoVia(turnoAux, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);
            }

            // Manda el comando ESTADO a pantalla para cerrar las subventanas correspondientes
            listaDatosVia = new List<DatoVia>();
            Causa causa = new Causa(eCausas.CausaCierre, eCausas.CausaCierre.ToString());
            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO, listaDatosVia);

            // Actualiza estado de vehiculo en pantalla, envío el ticket fiscal
            listaDatosVia = new List<DatoVia>();
            Vehiculo vehiculo = new Vehiculo();
            if (_turno.Mantenimiento == 'S')
                vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
            else
            {
                vehiculo.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
                vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
            }
            vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

            _loggerTransitos?.Info($"C;{DateTime.Now.ToString("HH:mm:ss.ff")};{_operadorActual.ID};{_turno.ModoAperturaVia};{_turno.Mantenimiento};{_turno.ModoQuiebre};{codCierre}");

            // Limpiamos operador actual
            _operadorActual = null;

            ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            if (!cierreAutomatico)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Cerrada"));
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Causa de Cierre") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(codCierre)));
            }

            if (origenComando == eOrigenComando.Supervision)
                ResponderComandoSupervision("E");

            // Limpio el vehiculo
            Vehiculo vehIngCat = _logicaVia.GetPrimerVehiculo();
            vehIngCat = new Vehiculo();

            //si era modo mantenimiento lo pasamos a false porque ya cerramos
            if (_esModoMantenimiento)
                _esModoMantenimiento = false;

            if (CerrandoTurno)
                CerrandoTurno = false;

            _ultimaAccion.Clear();
            _logger?.Info("Cierre de Turno. Causa: [{0}] - CierreAutomatico [{1}] - OrigenComando [{2}]", ClassUtiles.GetEnumDescr(codCierre), cierreAutomatico ? "S" : "N", origenComando.ToString());
        }

        #endregion

        #region Categorización

        /// <summary>
        /// Procesa la tecla CATEGO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaCategoria(ComandoLogica comando)
        {
            try
            {
                if (_ultimaAccion.AccionActual() == enmAccion.T_CASH)
                {
                    _logger?.Debug("LogicaCobroManual::TeclaCategoria -> No procesar Tecla, Accion actual es [{0}]", _ultimaAccion.AccionActual());
                }
                if (_ultimaAccion.AccionActual() == enmAccion.T_TARJETACREDITO)
                {
                    _logger?.Debug("LogicaCobroManual::TeclaCategoria -> No procesar Tecla, Accion actual es [{0}]", _ultimaAccion.AccionActual());
                }
                else
                {
                    //Si ya pasó mucho tiempo desde la última acción limpiamos
                    if (_ultimaAccion.AccionVencida())
                    {
                        _ultimaAccion.Clear();
                        _logger?.Debug("LogicaCobroManual::TeclaCategoria -> Clear Ultima Accion [{0}]", _ultimaAccion.AccionActual());
                    }

                    if (_turno.EstadoTurno == enmEstadoTurno.Quiebre)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EnQuiebre);
                    else
                    {
                        Vehiculo vehiculoPantalla = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);

                        _logger?.Debug("LogicaCobroManual::TeclaCategoria -> Cat[{0}]", vehiculoPantalla.Categoria);

                        // MGO
                        //if( ModeloPermite(ePermisosModos.CategorizacionCompuesta) )
                        //{
                        //    ModuloPantalla.Instance.EnviarDatos( enmStatus.Ok, enmAccion.CATEGO_COMPUESTA, JsonConvert.SerializeObject( _operacion ) );
                        //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("Ingrese la cantidada de Ejes") );
                        //}
                        //else
                        if (vehiculoPantalla.Categoria == 10)
                        {
                            ProcesarCantidadEjes(eTipoComando.eTecla, eCausas.IngresoEjes);
                        }
                        else
                        {
                            await Categorizar(vehiculoPantalla.Categoria);
                        }
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _loggerExcepciones?.Error(jsonEx);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda categorizar al vehiculo
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesCategorizacion(Vehiculo vehiculo)
        {
            bool retValue = true;
            bool esPrimero = false;

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.CategorizacionManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "(" + Traduccion.Traducir("Categorizacion Manual") + ")");

                    retValue = false;
                }
                else if (vehiculo.EstaPagado || ImprimiendoTicket)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);

                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                    retValue = false;
                }
                else if (vehiculo.CobroEnCurso)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CobroEnCurso);

                    retValue = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();
                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }
            }
            return retValue;
        }

        /// <summary>
        /// Se asigna la categoria recibida por pantalla al vehiculo
        /// </summary>
        /// <param name="categoria"></param>
        public override async Task Categorizar(short categoria, bool mostrarMensajePantalla = true)
        {
            bool esPrimero = false;
            bool esRetabulacion = false;

            try
            {
                ulong NroVehiculo = 0;
                Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);
                NroVehiculo = vehiculo.NumeroVehiculo;

                if (ValidarPrecondicionesCategorizacion(vehiculo))
                {
                    ModuloPantalla.Instance.LimpiarTodo(true);
                    ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.AbrirVia);
                    ModuloPantalla.Instance.LimpiarMensajes();
                    TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(categoria, vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Nada ? (byte)vehiculo.InfoTag.TipoTarifa : (byte)0);
                    // Se busca la tarifa
                    Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                    //Vuelvo a buscar el vehiculo
                    if (NroVehiculo > 0)
                        ConfirmarVehiculo(NroVehiculo, ref vehiculo, false);
                    else
                    {
                        vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);
                        NroVehiculo = vehiculo.NumeroVehiculo;
                    }

                    //Capturo foto y video
                    _logicaVia.DecideCaptura(eCausaVideo.Categorizar, vehiculo.NumeroVehiculo);

                    if (vehiculo.EstaPagado)
                    {
                        _logger.Info("Categorizar -> Vehiculo [{0}] ya esta pagado, salgo", vehiculo.NumeroVehiculo);
                        return;
                    }

                    // Si el vehiculo ya estaba categorizado
                    if (vehiculo.Categoria > 0)
                    {
                        esRetabulacion = true;
                        // Sumo las retabulaciones anteriores
                        vehiculo.HistorialCategorias = vehiculo.HistorialCategorias + " " + vehiculo.CategoDescripcionLarga;

                        // Genera evento de recategorizacion
                        //Vehiculo vehRetabulacion = vehiculo;
                        //vehRetabulacion.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito) + 1;
                        //ModuloEvebntos.Instance.SetRetabulacion(_turno, vehRetabulacion);
                        //vehiculo.NumeroTransito = 0; //devuelvo valor que tenía el vehiculo referenciado
                        _logger.Info("Retabulacion NroVeh[{0}] ViejaCat[{1}] NuevaCat[{2}]", vehiculo.NumeroVehiculo, vehiculo.Categoria, categoria);
                    }

                    // Si existe una tarifa
                    if (tarifa != null && tarifa.Valor > 0)
                    {
                        vehiculo.Categoria = Convert.ToInt16(tarifa.CodCategoria);
                        _logicaVia.GetVehOnline().CategoriaProximo = vehiculo.Categoria;
                        vehiculo.CategoByRunner = true;
                        vehiculo.Federado = false;
                        vehiculo.TransitoUFRE = false;
                        vehiculo.Tarifa = tarifa.Valor;
                        vehiculo.DesCatego = tarifa.Descripcion;
                        vehiculo.CategoDescripcionLarga = tarifa.DescripcionLarga;
                        vehiculo.TipoDiaHora = tarifa.CodigoHorario;
                        vehiculo.AfectaDetraccion = tarifa.AfectaDetraccion;
                        vehiculo.ValorDetraccion = 0;
                        vehiculo.EstadoDetraccion = 0;
                        vehiculo.InfoPagado.CargarValores(vehiculo);//se almacena la informacion de la categorizacion


                        // Se envia a pantalla para mostrar los datos del vehiculo
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);


                        if (mostrarMensajePantalla)
                        {
                            // Limpio los mensajes de las lineas por si quedo algo de antes
                            ModuloPantalla.Instance.LimpiarMensajes();
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Categoria Tabulada") + ": " + vehiculo.CategoDescripcionLarga);

                        }

                        _logger?.Debug("Se categorizó:" + vehiculo.Categoria + " (" + vehiculo.CategoDescripcionLarga + ") - VehNro " + " [" + vehiculo.NumeroVehiculo + "]");

                        // Via pasa a estado categorizada
                        _estado = eEstadoVia.EVAbiertaCat;

                        if (esRetabulacion)
                        {
                            // Genera evento de recategorizacion
                            Vehiculo vehRetabulacion = new Vehiculo();
                            vehRetabulacion.CopiarVehiculo(ref vehiculo);
                            vehRetabulacion.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito) + 1;
                            ModuloEventos.Instance.SetRetabulacion(_turno, vehRetabulacion);
                        }

                        

                        // Se envia mensaje al display
                        ModuloDisplay.Instance.Enviar(eDisplay.CAT, vehiculo);

                        // Se envia mensaje a modulo de video continuo
                        ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.Tabulacion, null, null, vehiculo);

                        ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                        // Update Online
                        ModuloEventos.Instance.ActualizarTurno(_turno);
                        UpdateOnline();

                        _logicaVia.LoguearColaVehiculos();


                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);
                    }
                    else
                    {
                        _logger.Info("Categorizar -> Error en busqueda de tarifa");
                    }
                }

            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }


        public async void ProcesarVuelto(ComandoLogica comando)
        {
            if(comando == null)
            {
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia);
            }
            else if (comando.CodigoStatus == enmStatus.Tecla)
            {
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia);
            }
            else if (comando.CodigoStatus == enmStatus.Ok)
                Vuelto(comando);
            else if (comando.CodigoStatus == enmStatus.Abortada)
            {
                _logicaVia.GetPrimerVehiculo().CobroEnCurso = false;

                if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
        }

        public async void Vuelto(ComandoLogica comando)
        {
            if (comando == null || comando.Accion == enmAccion.T_VUELTO && comando.CodigoStatus != enmStatus.Abortada)
            {
                decimal vuelto = 0;
                Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo();
                int monto = ClassUtiles.ExtraerObjetoJson<int>(comando.Operacion);
                if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                {
                    vuelto = monto - (vehiculo.Tarifa + vehiculo.ValorDetraccion);
                }
                else
                {
                    vuelto = monto - vehiculo.Tarifa;
                }
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vuelto, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(monto, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia);

                if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
            if (comando.Accion == enmAccion.T_CATEGORIAESPECIAL && comando.CodigoStatus == enmStatus.Abortada)
            {
                if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
        }

        public void ProcesarCantidadEjes(eTipoComando tipoComando, eCausas eCausa)
        {
            if (tipoComando == eTipoComando.eTecla)
            {
                Causa causa = new Causa();

                causa.Codigo = eCausa;
                causa.Descripcion = Traduccion.Traducir(ClassUtiles.GetEnumDescr(eCausa));

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                // Se pide el ingreso de patente
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAESPECIAL, listaDatosVia);
            }
        }

        #endregion

        #region Pagado Efectivo

        /// <summary>
        /// Procesa la tecla EFECTIVO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaPagoEfectivo(ComandoLogica comando)
        {
            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
            //Si ya pasó mucho tiempo desde la última acción limpiamos
            if (!_ultimaAccion.MismaAccion(enmAccion.T_CASH) || _ultimaAccion.AccionVencida())
                _ultimaAccion.Clear();

            if (!_ultimaAccion.AccionEnProceso())
            {
                // Para que no se pueda asignar un tag
                if (_logicaVia.GetPrimerVehiculo().Categoria > 0)
                    _logicaVia.GetPrimerVehiculo().CobroEnCurso = true;

                _logger.Debug("TeclaPagoEfectivo -> Accion Actual: {0}", enmAccion.T_CASH);
                _ultimaAccion.GuardarAccionActual(enmAccion.T_CASH);

                await PagadoEfectivo();
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda pagar en efectivo un vehiculo
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesPagadoEfectivo()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }

                bool categoFPagoValida = await CategoriaFormaPagoValida('E', ' ', vehiculo.Categoria);

                if (retValue && !categoFPagoValida)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                    retValue = false;
                }
                else if (_logicaVia.EstaOcupadoSeparadorSalida())// _logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);

                    retValue = false;
                }
                else if (vehiculo.InfoTag.PagoEnVia == 'S')
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Vehiculo Pago en Via");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Realice el cobro con la tecla Viaje");
                    return false;
                }
                else if (ModoPermite(ePermisosModos.PatenteObligatoria) && string.IsNullOrEmpty(vehiculo.Patente) && string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                    retValue = false;
                }
                else if(vehiculo.AfectaDetraccion == 'S' && (vehiculo.EstadoDetraccion == 3 || vehiculo.EstadoDetraccion == 2 || vehiculo.EstadoDetraccion == 1))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "No se puede emitir Boleta con Detraccion");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Cobre con Factura");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "O vuelva a ingresar la categoria");
                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }
                else if (vehiculo.InfoTag.GetTagHabilitado() && (vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Nada) && vehiculo.InfoTag.TipOp != 'C')
                //vehiculo.InfoTag.ErrorTag == eErrorTag.SinSaldo ||
                //vehiculo.InfoTag.TipBo == 'P')
                //vehiculo.TransitoUFRE)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TieneTag);

                    retValue = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();
                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }
                else if (vehiculo.TipoVenta != eVentas.Nada)
                    retValue = false;
                /*else if( VerificarStatusImpresora() )
                {
                    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir(ClassUtiles.GetEnumDescr( errorImpresora )) );

                    retValue = false;
                }*/
            }
            return retValue;
        }

        /// <summary>
        /// Se cambia el estado del vehiculo a pagado y si se debe, se imprime el ticket 
        /// </summary>
        private async Task PagadoEfectivo()
        {
            ulong NroVehiculo = 0;
            InfoCliente cliente = new InfoCliente();
            InfoPagado oPagado;

            try
            {
                Vehiculo vehiculo;

                vehiculo = _logicaVia.GetPrimerVehiculo();

                if (await ValidarPrecondicionesPagadoEfectivo())
                {
                    oPagado = vehiculo.InfoPagado;
                    NroVehiculo = vehiculo.NumeroVehiculo;
                    vehiculo.CobroEnCurso = true;
                    bool facturaEnviadaImprimir = false;

                    

                    //Capturo foto y video
                    _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);

                    _logger.Info($"PagadoEfectivo -> Se validaron las precondiciones. NroVeh [{NroVehiculo}]");

                    if (oPagado.Categoria > 0)
                    {
                        // Se busca la tarifa si no tenía almacenada 
                        //(es un tag pago en via hay que buscar otra vez la tarifa sin descuentos.)
                        // if (oPagado.InfoTag != null && oPagado.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                        // {
                        TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);
                        Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                        oPagado.Tarifa = tarifa.Valor;
                        oPagado.DesCatego = tarifa.Descripcion;
                        oPagado.CategoDescripcionLarga = tarifa.Descripcion;
                        oPagado.TipoDiaHora = tarifa.CodigoHorario;
                        oPagado.AfectaDetraccion = tarifa.AfectaDetraccion;
                        //Si se presiono la tecla detraccion manual el estado es '3' y se cobra la detraccion
                        if (oPagado.AfectaDetraccion == 'S')
                        {
                            TarifaABuscar buscardetraccion = GenerarTarifaABuscar(vehiculo.Categoria, 9);
                            Tarifa detraccion = await ModuloBaseDatos.Instance.BuscarTarifaAsync(buscardetraccion);
                            tarifa.ValorDetraccion = detraccion.Valor;
                            oPagado.ValorDetraccion = detraccion.Valor;
                        }
                        else
                            oPagado.ValorDetraccion = 0;
                        //  }

                        if (oPagado.Tarifa > 0)
                        {
                            //Limpio la cuenta de peanas del DAC
                            DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                            //Vuelvo a buscar el vehiculo
                            ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                            oPagado.NoPermiteTag = true;
                            oPagado.TipOp = 'E';
                            oPagado.TipBo = ' ';
                            oPagado.FormaPago = eFormaPago.CTEfec;
                            oPagado.Fecha = Fecha;
                            oPagado.FechaFiscal = oPagado.Fecha;

                            if (oPagado.InfoTag != null)
                            {
                                oPagado.Patente = oPagado.InfoTag.Patente == oPagado.Patente ? "" : oPagado.Patente;
                                oPagado.InfoTag?.Clear();
                                vehiculo.InfoTag?.Clear();
                            }

                            if (_esModoMantenimiento)
                            {
                                oPagado.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                                if (oPagado.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                    oPagado.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                            }
                            else
                            {
                                oPagado.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);
                                if (oPagado.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                    oPagado.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                            }

                            cliente.RazonSocial = "CONSUMIDOR FINAL";
                            cliente.Ruc = "99999999999";
                            cliente.Clave = 0;
                            oPagado.InfoCliente = cliente;
                            if (vehiculo.InfoOCRDelantero.Patente != "")
                                oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;

                            vehiculo.CargarDatosPago(oPagado, true);//se cargan los datos necesarios para generar clave
                            oPagado.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                            _loggerTransitos?.Info($"P;{oPagado.Fecha.ToString("HH:mm:ss.ff")};{oPagado.Categoria};{oPagado.TipOp};{oPagado.TipBo};{vehiculo.GetSubFormaPago()};{oPagado.Tarifa};{oPagado.NumeroTicketF};{vehiculo.Patente};{vehiculo.InfoTag.NumeroTag};{oPagado.InfoCliente.Ruc};0");

                            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                            ModuloPantalla.Instance.LimpiarMensajes();

                            //Finaliza lectura de Tchip
                            ModuloTarjetaChip.Instance.FinalizaLectura();

                            //Vuelvo a buscar el vehiculo
                            ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                            //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                            if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                            && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                            {
                                vehiculo.CobroEnCurso = false;
                                vehiculo.LimpiarDatosClave();
                                oPagado.ClearDatosFormaPago();

                                if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);

                                if (_esModoMantenimiento)
                                {
                                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                                }
                                else
                                {
                                    ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                                    _logger.Debug($"PagadoEfectivo -> Decrementa nro ticket. Actual [{ulNro}]");
                                }

                                // Envia el error de impresora a pantalla
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                                _logger.Info("PagadoEfectivo -> " + Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                            }
                            else
                            {
                                Vehiculo oVehAux = new Vehiculo();

                                //verificar que el veh sigue disponible, si no lo está asignamos el pagado al veh de atras
                                if (vehiculo.ProcesandoViolacion)
                                {
                                    _logger.Debug($"PagadoEfectivo -> Se está procesando una violación en el veh [{vehiculo.NumeroVehiculo}]");
                                    vehiculo.LimpiarDatosClave();
                                    vehiculo = _logicaVia.GetPrimerVehiculo();
                                    //Actualizo el numero de vehiculo
                                    NroVehiculo = vehiculo.NumeroVehiculo;
                                    _logger.Debug($"PagadoEfectivo -> Se agregan datos del Pagado al vehiculo siguiente, veh [{vehiculo.NumeroVehiculo}]");
                                }

                                _estado = eEstadoVia.EVAbiertaPag;

                                if (vehiculo.InfoOCRDelantero.Patente != "")
                                    oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;

                                vehiculo.TarifaOriginal = oPagado.Tarifa;
                                vehiculo.IVA = oPagado.Tarifa * 18 / 118;
                                vehiculo.IVA = Decimal.Round(vehiculo.IVA, 2);

                                //se cargan los datos al vehiculo, vehAux se utiliza para las operaciones
                                vehiculo.CargarDatosPago(oPagado);
                                oVehAux.CopiarVehiculo(ref vehiculo);
                                oVehAux.EstadoDetraccion = vehiculo.EstadoDetraccion;

                                //Genero el ticket legible 
                                await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Efectivo, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                                oVehAux.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                                //Se incrementa tránsito
                                oVehAux.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                                if (oVehAux.AfectaDetraccion == 'S' && (oVehAux.EstadoDetraccion == 1 || oVehAux.EstadoDetraccion == 2 || oVehAux.EstadoDetraccion == 3))
                                {
                                    oVehAux.NumeroDetraccion = oPagado.NumeroDetraccion;
                                    oVehAux.ValorDetraccion = vehiculo.ValorDetraccion;
                                }

                                // Se actualizan perifericos
                                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                                _logger?.Debug("PagadoEfectivo -> BARRERA ARRIBA!!");

                                // Actualiza el estado de los mimicos en pantalla
                                Mimicos mimicos = new Mimicos();
                                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                                // Actualiza el mensaje en pantalla
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoEfectivo);

                                // Actualiza el estado de vehiculo en pantalla
                                listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(oVehAux, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                                
                                //almacena la foto
                                ModuloFoto.Instance.AlmacenarFoto(ref oVehAux);
                                // Envia mensaje a display
                                ModuloDisplay.Instance.Enviar(eDisplay.PAG);

                                //Vuelvo a buscar el vehiculo
                                ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                                //Almacenar transito en bd local
                                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(oVehAux, _turno);

                                //Vuelvo a buscar el vehiculo
                                ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                                //Se envía setCobro
                                oVehAux.Operacion = "CB";
                                ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux.InfoCliente, oVehAux);
                                ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux);

                                //Vuelvo a buscar el vehiculo
                                ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                                //copiar datos faltantes al vehiculo
                                vehiculo.Operacion = oVehAux.Operacion;
                                vehiculo.TicketLegible = oVehAux.TicketLegible;
                                vehiculo.NumeroTransito = oVehAux.NumeroTransito;

                                //Si tiene detraccion y esta pagada
                                if (oVehAux.AfectaDetraccion == 'S' && oVehAux.EstadoDetraccion == 3)
                                    vehiculo.NumeroDetraccion = oVehAux.NumeroDetraccion;

                                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                                // Adelantar Vehiculo
                                _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                                // Se envia mensaje a modulo de video continuo
                                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoEfectivo, null, null, vehiculo);

                                ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                                // Update Online
                                ModuloEventos.Instance.ActualizarTurno(_turno);

                                {
                                    ImprimiendoTicket = true;
                                    facturaEnviadaImprimir = true;
                                    errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Efectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                                    ImprimiendoTicket = false;
                                }

                                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                                    _logger.Info("PagadoEfectivo -> Impresion OK");
                                else
                                {
                                    if (facturaEnviadaImprimir)
                                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("FALLA DE IMPRESION DE FACTURA"));
                                    _logger.Info("PagadoEfectivo -> Impresion ERROR [{0}]", errorImpresora.ToString());
                                }

                                vehiculo.CobroEnCurso = false;
                                UpdateOnline();
                                _logicaVia.GrabarVehiculos();
                            }
                        }
                        else
                        {
                            vehiculo.CobroEnCurso = false;
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TarifaErrorBusqueda);
                            _logger.Info("PagadoEfectivo -> Hubo un problema al consultar la tarifa");
                        }
                    }
                    else
                    {
                        vehiculo.CobroEnCurso = false;
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);
                        _logger.Info("PagadoEfectivo -> El Vehiculo no tiene categoria, no continuamos");
                    }
                    _logicaVia.LoguearColaVehiculos();

                    ModuloPantalla.Instance.LimpiarMensajes();

                    if (vehiculo.EstaPagado)
                    {
                        List<ListadoEncuestas> encuestas = new List<ListadoEncuestas>();
                        encuestas = await ModuloBaseDatos.Instance.BuscarEncuestasAsync(ModuloBaseDatos.Instance.ConfigVia, vehiculo);

                        //Se filtran solo las preguntas que correspondan a la fecha y a la categoria del vehiculo
                        encuestas = encuestas.Where(tm => tm.FechaFin > DateTime.Now && tm.FechaIni < DateTime.Now && tm.Categoria == vehiculo.CategoDescripcionLarga).ToList();

                        if (encuestas.Count > 0)
                            MostrarEncuesta(encuestas, vehiculo);
                        else
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                    }
                }
                else
                {
                    vehiculo.CobroEnCurso = false;
                    List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia2);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
                }



                _logger.Debug("PagadoEfectivo -> Salgo");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _ultimaAccion.Clear();
        }

        #endregion       

        #region Pagado Tarjeta Credito

        public async void ProcesarFacturaTarjeta(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Ok)
            {
                InfoCliente infoCliente = ClassUtiles.ExtraerObjetoJson<InfoCliente>(comando.Operacion);
                EstadoFactura estadoFactura = ClassUtiles.ExtraerObjetoJson<EstadoFactura>(comando.Operacion);
                _logicaVia.GetPrimerVehiculo().InfoCliente = infoCliente;
                await InicioCobroTarjetaCredito();
            }
        }

        public async void ProcesarTagTarjeta(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Ok)
            {
                TagBD tagBD = ClassUtiles.ExtraerObjetoJson<TagBD>(comando.Operacion);
                //_logicaVia.GetPrimerVehiculo().InfoCliente = infoCliente;
                await InicioCobroTarjetaCredito();
            }
        }

        /// <summary>
        /// Procesa la tecla TARJETACREDITO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaTarjetaCredito(ComandoLogica comando)
        {
            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
            //Si ya pasó mucho tiempo desde la última acción limpiamos
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "TarjetaCredito");
            if (!_ultimaAccion.MismaAccion(enmAccion.T_TARJETACREDITO) || _ultimaAccion.AccionVencida())
                _ultimaAccion.Clear();

            if (!_ultimaAccion.AccionEnProceso())
            {
                _logger.Debug("TeclaTarjetaCredito -> Accion Actual: {0}", enmAccion.T_TARJETACREDITO);
                _ultimaAccion.GuardarAccionActual(enmAccion.T_TARJETACREDITO);

                await InicioCobroTarjetaCredito();
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda pagar con tarjeta de credito un vehiculo
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesTarjetaCredito()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }

                bool categoFPagoValida = await CategoriaFormaPagoValida('S', ' ', vehiculo.Categoria); //Verificar

                if (retValue && !categoFPagoValida)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                    retValue = false;
                }
                else if (_logicaVia.EstaOcupadoSeparadorSalida())// _logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);

                    retValue = false;
                }
                else if (vehiculo.InfoTag.PagoEnVia == 'S')
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Vehiculo Pago en Via");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Realice el cobro con la tecla Viaje");
                    return false;
                }
                else if (ModoPermite(ePermisosModos.PatenteObligatoria) && string.IsNullOrEmpty(vehiculo.Patente) && string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }
                else if (vehiculo.InfoTag.GetTagHabilitado() && (vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Nada) && vehiculo.InfoTag.TipOp != 'C')
                //vehiculo.InfoTag.ErrorTag == eErrorTag.SinSaldo ||
                //vehiculo.InfoTag.TipBo == 'P')
                //vehiculo.TransitoUFRE)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TieneTag);

                    retValue = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();
                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }
                else if (vehiculo.TipoVenta != eVentas.Nada)
                    retValue = false;
                /*else if( VerificarStatusImpresora() )
                {
                    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir(ClassUtiles.GetEnumDescr( errorImpresora )) );

                    retValue = false;
                }*/
            }

            return retValue;
        }
        /// <summary>
        /// Se cambia el estado del vehiculo a pagado y si se debe, se imprime el ticket 
        /// </summary>
        private async Task InicioCobroTarjetaCredito()
        {
            // QUITAR CUANDO SE IMPLEMENTE *******************************************************
            _ultimaAccion.Clear();
            _logicaVia.GetPrimerVehiculo().CobroEnCurso = false;
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Funcion aun no implementada");
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            _logger.Debug("TeclaTarjetaCredito -> Accion Actual: Salgo");
            return;
            // ***********************************************************************************

            ulong NroVehiculo = 0;
            InfoCliente cliente = new InfoCliente();
            InfoPagado oPagado;

            try
            {
                Vehiculo vehiculo;

                vehiculo = _logicaVia.GetPrimerVehiculo();

                if (await ValidarPrecondicionesTarjetaCredito())
                {
                    oPagado = vehiculo.InfoPagado;
                    NroVehiculo = vehiculo.NumeroVehiculo;
                    vehiculo.CobroEnCurso = true;
                    bool facturaEnviadaImprimir = false;

                    _logger.Info($"InicioCobroTarjetaCredito -> Se validaron las precondiciones. NroVeh [{NroVehiculo}]");

                    if (oPagado.Categoria > 0)
                    {
                        // Se busca la tarifa si no tenía almacenada 
                        //(es un tag pago en via hay que buscar otra vez la tarifa sin descuentos.)
                        // if (oPagado.InfoTag != null && oPagado.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                        // {
                        TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);
                        Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                        oPagado.Tarifa = tarifa.Valor;
                        oPagado.DesCatego = tarifa.Descripcion;
                        oPagado.CategoDescripcionLarga = tarifa.Descripcion;
                        oPagado.TipoDiaHora = tarifa.CodigoHorario;
                        oPagado.AfectaDetraccion = tarifa.AfectaDetraccion;
                        if (oPagado.AfectaDetraccion == 'S')
                        {
                            TarifaABuscar buscardetraccion = GenerarTarifaABuscar(vehiculo.Categoria, 9);
                            Tarifa detraccion = await ModuloBaseDatos.Instance.BuscarTarifaAsync(buscardetraccion);
                            tarifa.ValorDetraccion = detraccion.Valor;
                            oPagado.ValorDetraccion = detraccion.Valor;
                        }
                        else
                            oPagado.ValorDetraccion = 0;
                        //  }

                        if (oPagado.Tarifa > 0)
                        {
                            //Limpio la cuenta de peanas del DAC
                            DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                            //Vuelvo a buscar el vehiculo
                            ConfirmarVehiculo(NroVehiculo, ref vehiculo);

                            oPagado.NoPermiteTag = true;
                            oPagado.TipOp = 'S';
                            if (vehiculo.InfoCliente.TipoDocumento == 1)
                                oPagado.TipBo = 'F';
                            else
                                oPagado.TipBo = ' ';
                            oPagado.FormaPago = eFormaPago.CTTCredito;
                            oPagado.Fecha = Fecha;
                            oPagado.FechaFiscal = oPagado.Fecha;

                            if (oPagado.InfoTag != null)
                            {
                                oPagado.Patente = oPagado.InfoTag.Patente == oPagado.Patente ? "" : oPagado.Patente;
                                oPagado.InfoTag?.Clear();
                                vehiculo.InfoTag?.Clear();
                            }

                            if (_esModoMantenimiento)
                            {
                                oPagado.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                                if (oPagado.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                    oPagado.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                            }
                            else
                            {
                                oPagado.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);
                                if (oPagado.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                    oPagado.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                            }

                            if (!string.IsNullOrEmpty(vehiculo.InfoCliente.Ruc))
                            {
                                cliente = vehiculo.InfoCliente;
                            }
                            else
                            {
                                cliente.RazonSocial = "CONSUMIDOR FINAL";
                                cliente.Ruc = "9999999999999";
                            }

                            oPagado.InfoCliente = cliente;
                            if (vehiculo.InfoOCRDelantero.Patente != "")
                                oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;

                            _logger.Debug("Iniciando cobro tarjeta de credito");
                            TurnoTC turnoTC = new TurnoTC();

                            turnoTC.Estacion = ModuloBaseDatos.Instance.ConfigVia.CodigoEstacion.ToString();
                            turnoTC.Via = ModuloBaseDatos.Instance.ConfigVia.NombreVia;
                            turnoTC.OperadorID = _turno.Operador.ID;

                            VehiculoTC vehiculoTC = new VehiculoTC();

                            if (oPagado.ValorDetraccion != 0 && vehiculo.EstadoDetraccion == 3)
                            {
                                vehiculoTC.Tarifa = vehiculo.Tarifa + oPagado.ValorDetraccion;
                            }
                            else
                                vehiculoTC.Tarifa = vehiculo.Tarifa;

                            vehiculoTC.Categoria = vehiculo.Categoria;
                            vehiculoTC.Fecha = DateTime.Now;
                            vehiculoTC.IdTelectronica = "";
                            vehiculoTC.NumeroTransaccion = "";
                            vehiculoTC.TraceUnique = "";

                            InfoClienteTC clienteTC = new InfoClienteTC();

                            clienteTC.Ruc = cliente.Ruc;
                            clienteTC.RazonSocial = cliente.RazonSocial;
                            clienteTC.Clave = cliente.Clave;
                            clienteTC.Activo = cliente.Activo;

                            ModuloTarjetaCredito.Instance.IniciarCobro(vehiculoTC, turnoTC, clienteTC);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Acercar tarjeta al lector");
                            vehiculo.CargarDatosPago(oPagado, true);//se cargan los datos necesarios para generar clave
                            List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia3);
                        }
                    }

                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        private void OnRespuestaServicioTC(eStatusTarjetaCredito estado, RespuestaTC comando)
        {
            bool fallaCritica = true;
            string sDebug = "";
            string sObs = "";

            RespuestaTC respuestaServicioTC = new RespuestaTC();

            respuestaServicioTC = comando;

            if (respuestaServicioTC.DescripcionTipoComando == "Compra" && respuestaServicioTC.Status == eStatusTC.Ok
                && respuestaServicioTC.CodigoRespuesta == 0 && respuestaServicioTC.TraceUnique == "")
                respuestaServicioTC.Status = eStatusTC.Error;

            if (respuestaServicioTC.Status == eStatusTC.Ok || respuestaServicioTC.CodigoRespuesta != 0)
            {
                if (respuestaServicioTC.DescripcionTipoComando == "Ack" && respuestaServicioTC.MensajeAprobacion.Contains("TRANSACCION NO EXISTE"))
                {
                    fallaCritica = false;
                    _logger.Info("OnRespuestaServicioTC() -> ACK recibido OK");
                }
                else if (respuestaServicioTC.DescripcionTipoComando == "AnulacionCompra")
                {
                    if (respuestaServicioTC.MensajeAprobacion.Contains("HOST NO RESPONDE") && respuestaServicioTC.Status == eStatusTC.Ok)
                    {
                        fallaCritica = false;
                        if (respuestaServicioTC.MensajeAprobacion.Contains("TRANSACCION CANCELADA"))
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Reintente: Seleccione cancelar nuevamente");
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Debe acercar la tarjeta para cancelar el pago");
                        }
                        else if (!string.IsNullOrEmpty(respuestaServicioTC.MensajeAprobacion))
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Reintente: Seleccione cancelar nuevamente");
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Debe acercar la tarjeta para cancelar el pago");
                        }
                        else
                            ModuloPantalla.Instance.LimpiarMensajes();
                    }
                    else
                    {
                        fallaCritica = false;
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Falla en cancelacion de pago tarjeta crédito/débito");
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Se mantiene pago tarjeta crédito/débito");

                        ModuloTarjetaCredito.Instance.AnulacionCobro(_logicaVia.GetPrimeroSegundoVehiculo(), _turno);
                    }
                }
                else if (respuestaServicioTC.DescripcionTipoComando == "Compra")
                {
                    fallaCritica = false;
                    if (respuestaServicioTC.MensajeAprobacion.Contains("TRANSACCION CANCELADA") ||
                        respuestaServicioTC.MensajeAprobacion.Contains("ERROR EN LECTURA DE TARJETA"))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Reintente: Seleccione forma de pago nuevamente");
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Debe acercar la tarjeta para realizar el pago");
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Falla en pago tarjeta crédito/débito");
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Reintente: Seleccione forma de pago nuevamente");
                    }
                }
                else if (respuestaServicioTC.DescripcionTipoComando == "Cierre")
                {
                    fallaCritica = false;
                    _logger.Info("OnRespuestaServicioTC() -> Falla en proceso de cierre. Se reintenta en 1 minuto");
                }

                if (fallaCritica)
                {
                    //Avisar a monitor para reiniciar Servicio                    
                }
            }
            else
            {
                if (respuestaServicioTC.DescripcionTipoComando == "Compra")
                {
                    PagadoTarjetaCredito();
                }
                else if (respuestaServicioTC.DescripcionTipoComando == "AnulacionCompra")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Cobro con tarjeta Anulado");
                    ModuloTarjetaCredito.Instance.AnulacionCobro(_logicaVia.GetPrimerVehiculo(), _turno);
                }
                else if (respuestaServicioTC.DescripcionTipoComando == "Cierre")
                {
                    _logger.Info("OnRespuestaServicioTC() -> Cierre finalizado");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Cierre de lote de tarjeta exitoso");
                }
            }

        }

        private async Task PagadoTarjetaCredito()
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            InfoPagado oPagado = new InfoPagado();
            bool facturaEnviadaImprimir = false;
            oPagado = vehiculo.InfoPagado;

            //Capturo foto y video
            _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);

            _loggerTransitos?.Info($"P;{oPagado.Fecha.ToString("HH:mm:ss.ff")};{oPagado.Categoria};{oPagado.TipOp};{oPagado.TipBo};{vehiculo.GetSubFormaPago()};{oPagado.Tarifa};{oPagado.NumeroTicketF};{vehiculo.Patente};{vehiculo.InfoTag.NumeroTag};{oPagado.InfoCliente.Ruc};0");

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

            ModuloPantalla.Instance.LimpiarMensajes();

            //Si falla la impresion, se limpian los datos correspondientes del vehiculo
            if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
            && !ModoPermite(ePermisosModos.TransitoSinImpresora))
            {
                vehiculo.CobroEnCurso = false;
                vehiculo.LimpiarDatosClave();
                oPagado.ClearDatosFormaPago();

                if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);

                if (_esModoMantenimiento)
                {
                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                }
                else
                {
                    ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                    _logger.Debug($"InicioCobroTarjetaCredito -> Decrementa nro ticket. Actual [{ulNro}]");
                }

                // Envia el error de impresora a pantalla
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                _logger.Info("InicioCobroTarjetaCredito -> " + Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
            }
            else
            {
                Vehiculo oVehAux = new Vehiculo();

                //verificar que el veh sigue disponible, si no lo está asignamos el pagado al veh de atras
                if (vehiculo.ProcesandoViolacion)
                {
                    _logger.Debug($"InicioCobroTarjetaCredito -> Se está procesando una violación en el veh [{vehiculo.NumeroVehiculo}]");
                    vehiculo.LimpiarDatosClave();
                    vehiculo = _logicaVia.GetPrimerVehiculo();
                    //Actualizo el numero de vehiculo
                    _logger.Debug($"InicioCobroTarjetaCredito -> Se agregan datos del Pagado al vehiculo siguiente, veh [{vehiculo.NumeroVehiculo}]");
                }

                _estado = eEstadoVia.EVAbiertaPag;

                if (vehiculo.InfoOCRDelantero.Patente != "")
                    oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;

                vehiculo.TarifaOriginal = oPagado.Tarifa;
                vehiculo.IVA = oPagado.Tarifa * 18 / 118;
                vehiculo.IVA = Decimal.Round(vehiculo.IVA, 2);

                //se cargan los datos al vehiculo, vehAux se utiliza para las operaciones
                vehiculo.CargarDatosPago(oPagado);
                oVehAux.CopiarVehiculo(ref vehiculo);
                oVehAux.EstadoDetraccion = vehiculo.EstadoDetraccion;

                //Genero el ticket legible 
                if (vehiculo.InfoCliente.Ruc.StartsWith("20") && vehiculo.InfoCliente.Ruc.Length == 11)
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Factura, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                else
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Efectivo, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                oVehAux.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                //Se incrementa tránsito
                oVehAux.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                if (oVehAux.AfectaDetraccion == 'S' && (oVehAux.EstadoDetraccion == 1 || oVehAux.EstadoDetraccion == 2 || oVehAux.EstadoDetraccion == 3))
                {
                    oVehAux.NumeroDetraccion = oPagado.NumeroDetraccion;
                    oVehAux.ValorDetraccion = vehiculo.ValorDetraccion;
                }

                // Se actualizan perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                _logger?.Debug("InicioCobroTarjetaCredito -> BARRERA ARRIBA!!");

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Actualiza el mensaje en pantalla
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.InicioCobroTarjetaCredito);

                // Actualiza el estado de vehiculo en pantalla
                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(oVehAux, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                
                //almacena la foto
                ModuloFoto.Instance.AlmacenarFoto(ref oVehAux);
                // Envia mensaje a display
                ModuloDisplay.Instance.Enviar(eDisplay.PAG);

                //Almacenar transito en bd local
                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(oVehAux, _turno);

                //Se envía setCobro
                oVehAux.Operacion = "CB";
                ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux.InfoCliente, oVehAux);
                ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux);


                //copiar datos faltantes al vehiculo
                vehiculo.Operacion = oVehAux.Operacion;
                vehiculo.TicketLegible = oVehAux.TicketLegible;
                vehiculo.NumeroTransito = oVehAux.NumeroTransito;

                //Si tiene detraccion y esta pagada
                if (oVehAux.AfectaDetraccion == 'S' && oVehAux.EstadoDetraccion == 3)
                    vehiculo.NumeroDetraccion = oVehAux.NumeroDetraccion;

                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                // Adelantar Vehiculo
                _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                // Se envia mensaje a modulo de video continuo
                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoEfectivo, null, null, vehiculo);

                ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                // Update Online
                ModuloEventos.Instance.ActualizarTurno(_turno);

                {
                    ImprimiendoTicket = true;
                    facturaEnviadaImprimir = true;
                    errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Efectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                    ImprimiendoTicket = false;
                }

                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                    _logger.Info("InicioCobroTarjetaCredito -> Impresion OK");
                else
                {
                    if (facturaEnviadaImprimir)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("FALLA DE IMPRESION DE FACTURA"));
                    _logger.Info("InicioCobroTarjetaCredito -> Impresion ERROR [{0}]", errorImpresora.ToString());
                }

                vehiculo.CobroEnCurso = false;
                UpdateOnline();
                _logicaVia.GrabarVehiculos();
            }

            _logicaVia.LoguearColaVehiculos();

            if (vehiculo.EstaPagado)
            {
                List<ListadoEncuestas> encuestas = new List<ListadoEncuestas>();
                encuestas = await ModuloBaseDatos.Instance.BuscarEncuestasAsync(ModuloBaseDatos.Instance.ConfigVia, vehiculo);

                //Se filtran solo las preguntas que correspondan a la fecha y a la categoria del vehiculo
                encuestas = encuestas.Where(tm => tm.FechaFin > DateTime.Now && tm.FechaIni < DateTime.Now && tm.Categoria == vehiculo.CategoDescripcionLarga).ToList();

                if (encuestas.Count > 0)
                    MostrarEncuesta(encuestas, vehiculo);
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
            }
            else
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
            }


            {
                vehiculo.CobroEnCurso = false;
            }

            _logger.Debug("InicioCobroTarjetaCredito -> Salgo");


            _ultimaAccion.Clear();
        }


        #endregion

        #region Pagado en Via

        /// <summary>
        /// Realiza el cobro para los prepagos que pagan en via
        /// </summary>
        private async Task PagadoEnVia()
        {
            try
            {
                _logger.Info("Pagado en Via -> Inicio");

                //Finaliza lectura de Tchip
                ModuloTarjetaChip.Instance.FinalizaLectura();
                InfoCliente cliente = new InfoCliente();
                ClienteDB clienteDB = new ClienteDB();

                Vehiculo vehiculo = _logicaVia.GetVehIng();
                ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
                _logger.Info("Pagado en Via -> Inicio Vehiculo [{0}]", ulNroVehiculo);

                //Capturo foto y video
                _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);

                if (string.IsNullOrEmpty(vehiculo.InfoTag?.NumeroTag) || (vehiculo.InfoTag?.TipoSaldo != 'V' && !vehiculo.InfoTag.PagoEnViaPrepago))
                {
                    //Vehiculo incorrecto
                    _logger.Info("Pagado en Via -> Tag vacio o NO pago en via");
                    return;
                }
                else if (vehiculo.EstaPagado)
                {
                    //Vehiculo incorrecto
                    _logger.Info("Pagado en Via -> vehiculo ya pagado");
                    return;
                }
                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    _logger.Info("Pagado en Via -> vehiculo Procesando Violacion");
                    return;
                }

                vehiculo.FormaPago = eFormaPago.CTBUFRE;

                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(vehiculo.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                vehiculo.CategoriaDesc = vehiculo.CategoDescripcionLarga;

                if (string.IsNullOrEmpty(vehiculo.CategoriaDesc))
                    vehiculo.CategoriaDesc = vehiculo.InfoTag.CategoDescripcionLarga;

                if (vehiculo.TipoTarifa != vehiculo.InfoTag.TipoTarifa)
                    vehiculo.TipoTarifa = vehiculo.InfoTag.TipoTarifa;

                if (vehiculo.Tarifa != vehiculo.InfoTag.Tarifa)
                    vehiculo.Tarifa = vehiculo.InfoTag.Tarifa;

                if (!string.IsNullOrEmpty(vehiculo.HistorialCategorias) && vehiculo.EsperaRecargaVia)
                {
                    vehiculo.Categoria = vehiculo.InfoTag.Categoria > 0 ? vehiculo.InfoTag.Categoria : vehiculo.InfoTag.CategoTabulada;
                    vehiculo.TipoDiaHora = vehiculo.InfoTag.TipDH;
                    vehiculo.Tarifa = vehiculo.InfoTag.Tarifa;
                    vehiculo.DesCatego = vehiculo.InfoTag.CategoDescripcionLarga;
                    vehiculo.CategoDescripcionLarga = vehiculo.InfoTag.CategoDescripcionLarga;
                    vehiculo.CategoriaDesc = vehiculo.InfoTag.CategoDescripcionLarga;
                    vehiculo.AfectaDetraccion = vehiculo.InfoTag.AfectaDetraccion;
                    vehiculo.ValorDetraccion = vehiculo.InfoTag.ValorDetraccion;

                    // Se envia a pantalla para mostrar los datos del vehiculo que indica el Tag
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                }

                vehiculo.NoPermiteTag = false;
                vehiculo.CobroEnCurso = false;
                vehiculo.TipOp = 'O';
                vehiculo.TipBo = 'U';
                vehiculo.FormaPago = eFormaPago.CTBUFRE;
                vehiculo.Fecha = Fecha;
                vehiculo.FechaFiscal = vehiculo.Fecha;
                vehiculo.EsperaRecargaVia = false;
                if (_esModoMantenimiento)
                    vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                else
                    vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);
                vehiculo.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                cliente.RazonSocial = vehiculo.InfoTag.NombreCuenta;
                cliente.Ruc = vehiculo.InfoTag.Ruc;
                cliente.Clave = (int)vehiculo.InfoTag.Cuenta;
                vehiculo.InfoCliente = cliente;
                vehiculo.IVA = vehiculo.Tarifa * 18 / 118;
                vehiculo.IVA = Decimal.Round(vehiculo.IVA, 2);

                clienteDB.Nombre = cliente.RazonSocial;
                clienteDB.NumeroDocumento = cliente.Ruc;

                //_loggerTransitos?.Info($"P;{vehiculo.Fecha.ToString("HH:mm:ss.ff")};{vehiculo.Categoria};{vehiculo.TipOp};{vehiculo.TipBo};{vehiculo.GetSubFormaPago()};{vehiculo.Tarifa};{vehiculo.NumeroTicketF};{vehiculo.Patente};{vehiculo.InfoTag.NumeroTag};{vehiculo.InfoCliente.Ruc};0");

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                ModuloPantalla.Instance.LimpiarMensajes();

                if (vehiculo.InfoCliente.Ruc[0] == '2' && vehiculo.InfoCliente.Ruc[1] == '0')
                {
                    vehiculo.NumeroFactura = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroFactura);

                    vehiculo.NumeroTicketF = 0;
                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                }


                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    vehiculo.NoPermiteTag = false;
                    vehiculo.CobroEnCurso = false;
                    //vehiculo.TipOp = ' ';
                    vehiculo.FormaPago = eFormaPago.Nada;
                    vehiculo.FechaFiscal = DateTime.MinValue;
                    vehiculo.ClaveAcceso = string.Empty;
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    {
                        vehiculo.NumeroDetraccion = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);
                    }

                    if (_esModoMantenimiento)
                    {
                        vehiculo.NumeroTicketNF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    }
                    else
                    {
                        vehiculo.NumeroTicketF = 0;
                        ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                        _logger.Debug($"PagadoEnVia -> Decrementa nro ticket. Actual [{ulNro}]");
                    }

                    // Envia el error de impresora a pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + ClassUtiles.GetEnumDescr(errorImpresora));
                }
                else
                {
                    Vehiculo oVehAux = new Vehiculo();

                    //verificar que el veh sigue disponible, si no lo está asignamos el pagado al veh de atras
                    if (vehiculo.ProcesandoViolacion)
                    {
                        _logger.Debug($"PagadoEnVia -> Se está procesando una violación en el veh [{vehiculo.NumeroVehiculo}]");
                        vehiculo.LimpiarDatosClave();
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                        //Actualizo el numero de vehiculo
                        ulNroVehiculo = vehiculo.NumeroVehiculo;
                        _logger.Debug($"PagadoEnVia -> Se agregan datos al vehiculo siguiente, veh [{vehiculo.NumeroVehiculo}]");
                    }

                    _estado = eEstadoVia.EVAbiertaPag;

                    oVehAux.CopiarVehiculo(ref vehiculo);
                    _estado = eEstadoVia.EVAbiertaPag;

                    //Genero el ticket legible 
                    if (vehiculo.InfoCliente.Ruc[0] == '2' && vehiculo.InfoCliente.Ruc[1] == '0')
                        await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Factura, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                    else
                        await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Efectivo, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                    //Se incrementa tránsito
                    vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                    // Se actualizan perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                    _logger?.Debug("PagadoEnVia -> BARRERA ARRIBA!!");

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    // Actualiza el mensaje en pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoEnVia);

                    _logger?.Debug(ClassUtiles.GetEnumDescr(eMensajesPantalla.PagadoEnVia));

                    // Actualiza el estado de vehiculo en pantalla
                    listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(clienteDB, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    
                    //almacena la foto
                    ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                    // Envia mensaje a display
                    ModuloDisplay.Instance.Enviar(eDisplay.PAG);

                    ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);
                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.TraTagManual);
                    //Se envía setCobro
                    vehiculo.Operacion = "CB";

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    //Si el vehiculo se fue y no esta en la fila, asigno al primero desocupado los datos
                    if (vehiculo.NumeroVehiculo != ulNroVehiculo)
                    {
                        eVehiculo eVeh = _logicaVia.BuscarVehiculo(ulNroVehiculo, false, true);
                        vehiculo = _logicaVia.GetVehiculo(eVeh);
                        _logger.Info("PagadoEnVia -> No se encontro Veh Nro[{Name}], asigno datos a [{Name}]", ulNroVehiculo, eVeh.ToString());
                    }
                    ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo.InfoCliente, vehiculo);
                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                    ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoTagChip, null, null, vehiculo, vehiculo.InfoTag);
                    ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                    _logicaVia.GetVehOnline().CategoriaProximo = 0;
                    _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                    // Adelantar Vehiculo
                    _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                    // Update Online
                    ModuloEventos.Instance.ActualizarTurno(_turno);

                    {
                        ImprimiendoTicket = true;
                        if (vehiculo.InfoCliente.Ruc[0] == '2' && vehiculo.InfoCliente.Ruc[1] == '0')
                            errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Factura, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null);
                        else
                            errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Efectivo, oVehAux, _turno, ModuloBaseDatos.Instance.ConfigVia, null);
                        ImprimiendoTicket = false;
                    }

                    if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                        _logger.Info("PagadoEnVia -> Impresion OK");
                    else
                    {
                        _logger.Info("PagadoEnVia -> Impresion ERROR [{0}]", errorImpresora.ToString());
                    }

                    UpdateOnline();
                    _logicaVia.GrabarVehiculos();
                }

                List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);

                _logger.Debug("PagadoEnVia -> Salgo");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _logicaVia.LoguearColaVehiculos();
        }

        #endregion

        #region Pagado Factura

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA FACTURA
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaFactura(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Tecla)
            {
                EstadoFactura estadoFactura = new EstadoFactura();
                estadoFactura.Codigo = eBusquedaFactura.BusquedaRut;

                await ProcesarFactura(null, estadoFactura);
            }
            else if (comando.CodigoStatus == enmStatus.Abortada)
            {
                _logicaVia.GetPrimerVehiculo().CobroEnCurso = false;

                if (_ultimaAccion.MismaAccion(enmAccion.FACTURA))
                    _ultimaAccion.Clear();
            }
        }

        /// <summary>
        /// Procesa la factura, se utiliza para varias instancias; cliente vacio, no validado y validado
        /// </summary>
        /// <param name="infoCliente"></param>
        /// <param name="estadoFactura"></param>
        private async Task ProcesarFactura(InfoCliente infoCliente, EstadoFactura estadoFactura)
        {
            //ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesFactura())
            {

                if (estadoFactura.Codigo != eBusquedaFactura.Confirma)
                {
                    // Se agrega este Clear, para el caso en que se hagan dos facturas seguidas
                    if (_ultimaAccion.MismaAccion(enmAccion.FACTURA))
                        _ultimaAccion.Clear();
                }

                string patente;
                patente = _logicaVia.GetPrimeroSegundoVehiculo().Patente;
                InfoCliente clientePatente = new InfoCliente();
                clientePatente.Patente = patente;
                List<InfoCliente> listaInfoCliente2 = await ObtenerListaClientes(clientePatente, eBusquedaFactura.BusquedaPatente);
                if(listaInfoCliente2.Count > 0 && estadoFactura.Codigo == eBusquedaFactura.BusquedaRut && infoCliente == null)
                {
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, listaDatosVia);
                    estadoFactura.Codigo = eBusquedaFactura.BusquedaPatente;
                    ClassUtiles.InsertarDatoVia(listaInfoCliente2, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, listaDatosVia);
                }

                // Cliente vacio, pido datos a pantalla
                if (infoCliente == null && estadoFactura.Codigo != eBusquedaFactura.BusquedaPatente)
                {
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, new List<DatoVia>());
                }
                // Nuevo Cliente
                else if (estadoFactura.Codigo == eBusquedaFactura.NuevoCliente)
                {
                    await AsignarNuevoClienteFactura(infoCliente);
                }
                //Busqueda por numero cliente
                else if (estadoFactura.Codigo == eBusquedaFactura.BusquedaNumeroCliente)
                {
                    // Busqueda por clave de cliente
                    if (infoCliente.Clave != 0)
                    {
                        List<InfoCliente> listaInfoCliente = await ObtenerListaClientes(infoCliente, eBusquedaFactura.BusquedaNumeroCliente);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaInfoCliente, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, listaDatosVia);
                    }
                }
                //Busqueda por razon social de cliente
                else if (estadoFactura.Codigo == eBusquedaFactura.BusquedaNombre)
                {
                    if (infoCliente.Nombre?.Length >= 5)
                    {
                        List<InfoCliente> listaInfoCliente = await ObtenerListaClientes(infoCliente, eBusquedaFactura.BusquedaNombre);
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaInfoCliente, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, listaDatosVia);
                    }
                }
                //Busqueda por rut
                else if (estadoFactura.Codigo == eBusquedaFactura.BusquedaRut)
                {
                    // Busqueda por RUC de cliente
                    if (!string.IsNullOrEmpty(infoCliente.Ruc))
                    {
                        List<InfoCliente> listaInfoCliente = await ObtenerListaClientes(infoCliente, eBusquedaFactura.BusquedaRut);
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaInfoCliente, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FACTURA, listaDatosVia);
                    }
                }
                //Cliente validado
                else if (estadoFactura.Codigo == eBusquedaFactura.Confirma)
                {
                    if (VerificarStatusImpresora(false))
                        await PagadoFactura(infoCliente);
                    else
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia3);
            }
        }

        /// <summary>
        /// Recibe un string JSON con los datos necesarios para validar la factura
        /// </summary>
        /// <param name="comando"></param>
        public override async void ValidarFactura(ComandoLogica comando)
        {
            try
            {
                InfoCliente infoCliente = ClassUtiles.ExtraerObjetoJson<InfoCliente>(comando.Operacion);
                EstadoFactura estadoFactura = ClassUtiles.ExtraerObjetoJson<EstadoFactura>(comando.Operacion);

                await ProcesarFactura(infoCliente, estadoFactura);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Asigna nuevo cliente y avisa al modulo de base de datos 
        /// para crear una nueva entrada en la tabla
        /// </summary>
        /// <param name="infoCliente"></param>
        private async Task AsignarNuevoClienteFactura(InfoCliente infoCliente)
        {
            try
            {
                ClienteDB cliente = new ClienteDB();

                cliente.TipoDocumento = 1; //TODO: Queda hardcodeado?
                cliente.Nombre = infoCliente.RazonSocial;
                cliente.Numero = ModuloBaseDatos.Instance.ObtenerUltimoNumeroCliente() + 1;
                cliente.NumeroDocumento = infoCliente.Ruc;

                // Grabar nuevo cliente en la base
                bool graboNuevoCliente = await ModuloBaseDatos.Instance.GrabarNuevoClienteAsync(cliente);

                if (graboNuevoCliente)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Nuevo cliente grabado"));
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No pudo grabarse nuevo cliente"));
                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Obtiene la lista de clientes a partir del RUC, clave o nombre 
        /// </summary>
        /// <param name="infoCliente"></param>
        /// <param name="busquedaFactura"></param>
        /// <returns></returns>
        private async Task<List<InfoCliente>> ObtenerListaClientes(InfoCliente infoCliente, eBusquedaFactura busquedaFactura)
        {
            ClienteABuscar clienteABuscar = new ClienteABuscar();

            switch (busquedaFactura)
            {
                case eBusquedaFactura.BusquedaRut:
                    clienteABuscar.NumeroDocumentoCliente = infoCliente.Ruc;
                    break;
                case eBusquedaFactura.BusquedaNumeroCliente:
                    clienteABuscar.NumeroCliente = infoCliente.Clave;
                    break;
                case eBusquedaFactura.BusquedaNombre:
                    clienteABuscar.NombreCliente = infoCliente.Nombre;
                    break;
                case eBusquedaFactura.BusquedaPatente:
                    clienteABuscar.PatenteCliente = infoCliente.Patente;
                    break;
                default:
                    break;
            }

            List<ClienteDB> listaClientes = await ModuloBaseDatos.Instance.BuscarClienteAsync(clienteABuscar);

            List<InfoCliente> listaInfoCliente = new List<InfoCliente>();

            if (listaClientes != null)
            {
                foreach (ClienteDB cliente in listaClientes)
                {
                    InfoCliente infoClienteAux = new InfoCliente();

                    infoClienteAux.Clave = cliente.Numero;
                    infoClienteAux.Nombre = cliente.Nombre?.Trim();
                    infoClienteAux.RazonSocial = cliente.Nombre?.Trim();
                    infoClienteAux.Ruc = cliente.NumeroDocumento?.Trim();
                    infoClienteAux.Direccion = cliente.Domicilio?.Trim();
                    infoClienteAux.Telefono = cliente.Telefono?.Trim();
                    infoClienteAux.TipoDocumento = cliente.TipoDocumento.Value;
                    infoClienteAux.Patente = cliente.Patente?.Trim();

                    listaInfoCliente.Add(infoClienteAux);
                }
            }

            return listaInfoCliente;
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda relizar un pago con factura
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesFactura()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);
                    retValue = false;
                }
                else if (ModoPermite(ePermisosModos.PatenteObligatoria) && string.IsNullOrEmpty(vehiculo.Patente) && string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);
                    retValue = false;
                }

                bool categoFPagoValida = await CategoriaFormaPagoValida('E', ' ', vehiculo.Categoria);

                if (retValue && !categoFPagoValida)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                    retValue = false;
                }
                else if (_logicaVia.EstaOcupadoSeparadorSalida()) //_logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    retValue = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();

                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }
                //else if( vehiculo.CobroEnCurso )
                //{
                //    retValue = false;
                //}

                //Borrar datos del chip y limpiar mensajes
                if (retValue && vehiculo.InfoTag.TipOp == 'C' && !vehiculo.EstaPagado && vehiculo.ListaRecarga.Count() == 0)
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ClearChip(vehiculo);
                }
            }
            return retValue;
        }

        /// <summary>
        /// Se cambia el estado del vehiculo a pagado y si se debe, se imprime el ticket 
        /// </summary>
        /// <param name="infoCliente"></param>
        private async Task PagadoFactura(InfoCliente infoCliente)
        {
            _logger.Info("PagadoFactura -> Inicio");

            InfoPagado oPagado;
            //Finaliza lectura de Tchip
            ModuloTarjetaChip.Instance.FinalizaLectura();
            Vehiculo vehiculo;

            ClienteDB cliente = new ClienteDB();

            vehiculo = _logicaVia.GetPrimerVehiculo();
            ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
            oPagado = vehiculo.InfoPagado;

            vehiculo.CobroEnCurso = true;
            bool facturaEnviadaImprimir = false;

            //Capturo foto y video
            _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);

            // Se recalcula la tarifa
            if (oPagado == null || oPagado.Categoria == 0 || oPagado.Tarifa < 0)
            {
                TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);
                Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                oPagado.Categoria = Convert.ToInt16(tarifa.CodCategoria);
                oPagado.Tarifa = tarifa.Valor;
                oPagado.DesCatego = tarifa.Descripcion;
                oPagado.CategoDescripcionLarga = tarifa.Descripcion;
                oPagado.TipoDiaHora = tarifa.CodigoHorario;
                oPagado.AfectaDetraccion = tarifa.AfectaDetraccion;
                if (oPagado.AfectaDetraccion == 'S')
                {
                    TarifaABuscar buscardetraccion = GenerarTarifaABuscar(vehiculo.Categoria, 9);
                    Tarifa detraccion = await ModuloBaseDatos.Instance.BuscarTarifaAsync(buscardetraccion);
                    tarifa.ValorDetraccion = detraccion.Valor;
                    oPagado.ValorDetraccion = detraccion.Valor;
                }
                else
                    oPagado.ValorDetraccion = 0;
            }

            if (oPagado.Tarifa > 0)
            {
                _estado = eEstadoVia.EVAbiertaPag;

                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                ulNroVehiculo = vehiculo.NumeroVehiculo;

                oPagado.NoPermiteTag = true;
                oPagado.TipOp = 'E';
                oPagado.TipBo = 'F';
                oPagado.FormaPago = eFormaPago.CTEfec;
                oPagado.Fecha = Fecha;
                oPagado.FechaFiscal = oPagado.Fecha;

                if (oPagado.InfoTag != null)
                {
                    oPagado.Patente = oPagado.InfoTag.Patente == oPagado.Patente ? "" : oPagado.Patente;
                    oPagado.InfoTag.Clear();
                    vehiculo.InfoTag.Clear();
                }

                if (_esModoMantenimiento)
                {
                    oPagado.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                    if (oPagado.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                        oPagado.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                }
                else
                {
                    oPagado.NumeroFactura = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroFactura);
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                        vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                }


                vehiculo.CargarDatosPago(oPagado, true);//se cargan los datos necesarios para generar clave
                oPagado.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                oPagado.InfoCliente = infoCliente;
                oPagado.InfoTag.NombreCuenta = infoCliente.RazonSocial;
                oPagado.InfoTag.Ruc = infoCliente.Ruc;
                oPagado.InfoTag.Direccion = infoCliente.Direccion;
                oPagado.InfoTag.Telefono = infoCliente.Telefono;
                if (vehiculo.InfoOCRDelantero.Patente != "")
                    oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;

                vehiculo.IVA = oPagado.Tarifa * 18 / 118;
                vehiculo.IVA = Decimal.Round(vehiculo.IVA, 2);
                cliente.Nombre = infoCliente.RazonSocial;
                cliente.NumeroDocumento = infoCliente.Ruc;
                cliente.Numero = infoCliente.Clave;
                cliente.TipoDocumento = 1; //TODO: Queda hardcodeado aca?

                //await GeneraClearing( oPagado, false );

                _loggerTransitos?.Info($"P;{oPagado.Fecha.ToString("HH:mm:ss.ff")};{oPagado.Categoria};{oPagado.TipOp};{oPagado.TipBo};{vehiculo.GetSubFormaPago()};{oPagado.Tarifa};{oPagado.NumeroTicketF};{oPagado.Patente};{oPagado.InfoTag.NumeroTag};{oPagado.InfoCliente.Ruc};0");

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                ModuloPantalla.Instance.LimpiarMensajes();

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                ulNroVehiculo = vehiculo.NumeroVehiculo;
                // Si falla la impresion, se limpian los datos del vehiculo
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                 && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    vehiculo.NoPermiteTag = false;
                    vehiculo.CobroEnCurso = false;
                    vehiculo.LimpiarDatosClave();
                    oPagado.ClearDatosFormaPago();
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    {
                        vehiculo.NumeroDetraccion = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);
                    }
                    if (_esModoMantenimiento)
                    {
                        vehiculo.NumeroTicketNF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    }
                    else
                    {
                        vehiculo.NumeroFactura = 0;
                        ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroFactura);
                        _logger.Debug($"PagadoFactura -> Decrementa nro ticket. Actual [{ulNro}]");
                    }

                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
                else
                {
                    if (vehiculo.InfoOCRDelantero.Patente != "")
                        oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;
                    if (vehiculo.AfectaDetraccion == 'S' && (vehiculo.EstadoDetraccion == 1 || vehiculo.EstadoDetraccion == 2 || vehiculo.EstadoDetraccion == 3))
                    {
                        oPagado.AfectaDetraccion = 'S';
                        oPagado.ValorDetraccion = vehiculo.ValorDetraccion;

                        ConfigVia config = ModuloBaseDatos.Instance.ConfigVia;
                        string codDetrac;
                        codDetrac = config.CodigoConcesion;
                        switch (config.NumeroDeEstacion)
                        {
                            case 1:
                                codDetrac += "FO";
                                break;
                            case 2:
                                codDetrac += "HU";
                                break;
                            case 3:
                                codDetrac += "VE";
                                break;
                            case 4:
                                codDetrac += "VI";
                                break;
                        }
                        string numeroDeViaString = config.NumeroDeVia.ToString();
                        codDetrac += numeroDeViaString[numeroDeViaString.Length - 1].ToString();
                        codDetrac += vehiculo.DesCatego.ToString();
                        if (oPagado.TipOp == 'E')
                            codDetrac += "EF";
                        else
                            codDetrac += "TA";
                        codDetrac += vehiculo.NumeroDetraccion.ToString("D6");
                        vehiculo.CodigoDetraccion = codDetrac;
                    }                    
                    
                    vehiculo.CargarDatosPago(oPagado);
                    vehiculo.InfoCliente = infoCliente;
                    //Consultar nuevamente por el ticket 
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Factura, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    ulNroVehiculo = vehiculo.NumeroVehiculo;
                    vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                    //Se incrementa tránsito
                    vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                    // Se actualizan los perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                    _logger?.Debug("PagadoFactura -> BARRERA ARRIBA!!");

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    // Actualiza el mensaje en pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoFactura);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);

                    _logger?.Debug(ClassUtiles.GetEnumDescr(eMensajesPantalla.PagadoFactura));

                    // Actualiza el estado de vehiculo en pantalla
                    listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(cliente, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    // Envia mensaje a display
                    ModuloDisplay.Instance.Enviar(eDisplay.PAG);

                    
                    //almacena la foto
                    ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                    _logicaVia.GetVehOnline().CategoriaProximo = 0;
                    _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                    // Adelantar Vehiculo
                    _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    //Se envía setCobro
                    vehiculo.Operacion = "CB";
                    ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, infoCliente, vehiculo);
                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                    // Se envia mensaje a modulo de video continuo
                    ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoEfectivo, null, null, vehiculo);

                    ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                    // Update Online
                    ModuloEventos.Instance.ActualizarTurno(_turno);
                    UpdateOnline();
                    //ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("Pagado Efectivo Factura") );

                    _logicaVia.GrabarVehiculos();

                    {
                        ImprimiendoTicket = true;
                        errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Factura, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);
                        ImprimiendoTicket = false;
                    }

                    if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                        _logger.Info("PagadoFactura -> Impresion OK");
                    else
                    {
                        _logger.Info("PagadoFactura -> Impresion ERROR [{0}]", errorImpresora.ToString());
                    }

                    // Se agrega este Clear, para el caso en que se hagan dos facturas seguidas
                    if (_ultimaAccion.MismaAccion(enmAccion.FACTURA))
                        _ultimaAccion.Clear();

                    vehiculo.CobroEnCurso = false;
                    UpdateOnline();
                    _logicaVia.GrabarVehiculos();

                    List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);
                }
            }
            else
            {
                vehiculo.CobroEnCurso = false;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TarifaErrorBusqueda);
                _logger.Info("PagadoFactura -> Hubo un problema al consultar la tarifa");
                //enviar error a subventana
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
            }

            //_ultimaAccion.Clear();
            _logicaVia.LoguearColaVehiculos();

            if (vehiculo.EstaPagado)
            {
                List<ListadoEncuestas> encuestas = new List<ListadoEncuestas>();
                encuestas = await ModuloBaseDatos.Instance.BuscarEncuestasAsync(ModuloBaseDatos.Instance.ConfigVia, vehiculo);

                //Se filtran solo las preguntas que correspondan a la fecha y a la categoria del vehiculo
                encuestas = encuestas.Where(tm => tm.FechaFin > DateTime.Now && tm.FechaIni < DateTime.Now && tm.Categoria == vehiculo.CategoDescripcionLarga).ToList();

                if (encuestas.Count > 0)
                    MostrarEncuesta(encuestas, vehiculo);
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
            }
            else
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
            }

            _logger.Info("PagadoFactura -> Fin");
        }

        #endregion

        #region Simulacion de Paso

        /// <summary>
        /// Procesa la tecla SIP enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaSimulacion(ComandoLogica comando)
        {
            await ProcesarSimulacion(eTipoComando.eTecla, eOrigenComando.Pantalla, null);
        }

        /// <summary>
        /// Procesa la simulacion del transito, 
        /// se utiliza para las instancias de tecla presionada y de seleccion de causa
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <param name="causaSimulacion"></param>
        private async Task ProcesarSimulacion(eTipoComando tipoComando, eOrigenComando origenComando, CausaSimulacion causaSimulacion)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesSimulacion(tipoComando, origenComando) || _bSIPforzado)
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    // Se consulta la lista de causas de simulacion al modulo de BD 
                    List<CausaSimulacion> listaCausasSimulacion = await ModuloBaseDatos.Instance.BuscarCausasSimulacionAsync();

                    // Si hay al menos una causa de simulacion
                    if (listaCausasSimulacion?.Count > 0)
                    {
                        ListadoOpciones opciones = new ListadoOpciones();

                        Causa causa = new Causa();
                        causa.Codigo = eCausas.CausaSimulacion;
                        causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.CausaSimulacion);

                        foreach (CausaSimulacion causas in listaCausasSimulacion.Where(x => x.EnVia == "S"))
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causas), true, causas.Descripcion, string.Empty, causas.Orden, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        // Envio la lista de causas de simulacion a pantalla
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    //else
                    //{
                    //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de simulacion" ));
                    //}
                }
                else if ((tipoComando == eTipoComando.eSeleccion ||
                          origenComando == eOrigenComando.Supervision) &&
                          causaSimulacion != null)
                {
                    _bSIPforzado = false;
                    SimulacionPaso(causaSimulacion, true);
                }

            }
            else
            {
                List<DatoVia> listaDatosVia3 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda simular el paso un vehiculo
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesSimulacion(eTipoComando tipoComando, eOrigenComando origenComando)
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else if (!vehiculo.EstaPagado)
            {
                if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
                {
                    ComandoLogica comando = new ComandoLogica();
                    TeclaBajaBarrera(comando);
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoNoPagado);

                retValue = false;
            }
            else if (_logicaVia.EstaOcupadoLazoSalida())
            {
                //ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);

                if (!_bSIPforzado)
                {
                    // Solicito confirmacion para hacer SIP
                    Causa causa = new Causa(eCausas.SimulacionPaso, ClassUtiles.GetEnumDescr(eCausas.SimulacionPaso));
                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
                }

                retValue = false;
            }

            //si estaba el flag del sipForzado y no esta pagado, seguramente ya salio el veh o se movió, no se permite el SIP
            if (!vehiculo.EstaPagado && _bSIPforzado)
                _bSIPforzado = false;

            return retValue;
        }

        /// <summary>
        /// Se simula el transito, actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        /// <param name="causa"></param>
        public override void SimulacionPaso(CausaSimulacion causaSimulacion, bool bajarBarrera = true, ulong ulNumVeh = 0, eVehiculo eVeh = eVehiculo.eVehP1)
        {
            _logger.Info($"Simulacion Paso Inicio -> NroVeh[{ulNumVeh}] Posicion[{eVeh.ToString()}]");

            Vehiculo veh = _logicaVia.GetPrimeroSegundoVehiculo();
            ulNumVeh = veh.NumeroVehiculo;


            /*if( ulNumVeh > 0 )
                veh = _logicaVia.GetVehiculo( _logicaVia.BuscarVehiculo( ulNumVeh ) );
            else if( bajarBarrera )
                veh = _logicaVia.GetPrimerVehiculo();
            else
                veh = _logicaVia.GetVehiculo( eVeh );*/

            // Si el veh esta vacio, suponemos que se envio el evento con el mismo, si no está pagado no deberia realizar SIP (el veh ya salió)
            if (!veh.NoVacio || !veh.EstaPagado)
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "No hay vehiculo pagado para simular");
                _logger.Info("Simulacion Paso -> Veh vacio o no está pagado");
                return;
            }

            // Se actualizan los perifericos
            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

            if (bajarBarrera)
            {
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                _logger?.Debug("SimulacionPaso -> BARRERA ABAJO!!");
            }

            //Limpio los mensajes de las lineas
            ModuloPantalla.Instance.LimpiarMensajes();

            // Incremento alarma de SIPxDACFail
            eCausaSimulacion causaSIP;
            if (Enum.TryParse(causaSimulacion.Codigo, out causaSIP))
            {
                if (causaSIP == eCausaSimulacion.FallaDac)
                {
                    ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.SIPxDACFail, 0);
                }
            }

            List<DatoVia> listaDatosVia = new List<DatoVia>();

            // Se actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            //Vehiculo veh;
            /* if (ulNumVeh > 0)
                 veh = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(ulNumVeh));
             else if (bajarBarrera)
                 veh = _logicaVia.GetPrimerVehiculo();
             else
                 veh = _logicaVia.GetVehiculo(eVeh);*/

            //Almacenamos transito en la BD local
            ModuloBaseDatos.Instance.AlmacenarTransitoTurno(veh, _turno);

            // Se limpia la pantalla luego de la salida del vehiculo
            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);

            // Se limpia posible alarma de Sip con error de loop
            ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.SIPxDACFail, 0);

            // Se envia un mensaje al display
            //ModuloDisplay.Instance.Enviar( eDisplay.BNV );

            if (bajarBarrera)
            {
                veh.TipoObservacion = eTipoObservacion.Simulacion;
                veh.CodigoSimulacion = byte.Parse(causaSimulacion.Codigo);
            }
            else
            {
                if (veh.NumeroVehiculo > 0)
                    eVeh = _logicaVia.BuscarVehiculo(ulNumVeh);

                _logicaVia.GetVehiculo(eVeh).TipoObservacion = eTipoObservacion.Simulacion;
                _logicaVia.GetVehiculo(eVeh).CodigoSimulacion = byte.Parse(causaSimulacion.Codigo);
            }

            //Capturo foto
            eCausaVideo causaVideo = eCausaVideo.Manual;
            _logicaVia.CapturaFoto(ref veh, ref causaVideo);
            //Detengo el video
            causaVideo = eCausaVideo.LazoSalidaDesocupado;
            _logicaVia.CapturaVideo(ref veh, ref causaVideo);

            // Adelantar Vehiculo
            //_logicaVia.AdelantarVehiculo(eMovimiento.eOpCerrada);

            // Se limpia la pantalla luego de la salida del vehiculo
            if (bajarBarrera)
                ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);

            Vehiculo vehiculo = new Vehiculo();
            vehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito); //+1 porque aun no se ha aumentado en logicaVia
            _logger?.Debug("SIP realizado, NroTran: {0}", vehiculo.NumeroTransito);

            //solo limpiamos la información del vehículo si el primero es el mismo
            if (_logicaVia.GetPrimerVehiculo().NumeroVehiculo == veh.NumeroVehiculo)
            {
                vehiculo.FormaPago = eFormaPago.Nada;
                vehiculo.CategoDescripcionLarga = veh.CategoDescripcionLarga;
                vehiculo.Patente = string.Empty;
                vehiculo.Fecha = veh.Fecha;
                vehiculo.InfoTag = veh.InfoTag;
                vehiculo.CobroEnCurso = false;
                vehiculo.CodigoSimulacion = veh.CodigoSimulacion;
                ModuloPantalla.Instance.LimpiarVehiculo(vehiculo);
            }

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.OperacionCerrada, null, null, vehiculo);

            // Se envia un mensaje al display
            ModuloDisplay.Instance.Enviar(eDisplay.BNV);

            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(veh, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ACTUALIZA_ULTVEH, listaDatosVia);

            //se busca nuevamente el veh antes de realizar OpCerradaEvento
            /*if (ulNumVeh > 0)
                veh = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(ulNumVeh));
            else if (bajarBarrera)
                veh = _logicaVia.GetPrimerVehiculo();
            else
                veh = _logicaVia.GetVehiculo(eVeh);*/

            // Generar el evento de transito
            // Lo genera logica de via
            if (bajarBarrera)
            {
                _logicaVia.OpCerradaEvento(1, ref veh);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Transito Simulado") + ". " + Traduccion.Traducir("Causa") + ": " + causaSimulacion.Descripcion);
            }
            else
                _logicaVia.OpCerradaEvento(2, ref veh);

            _logger?.Debug("SIP realizado, vehiculo: {0}. Causa: {1}", veh.NumeroVehiculo, causaSimulacion.Descripcion);
            _loggerTransitos?.Info($"S;{Fecha.ToString("HH:mm:ss.ff")};{veh.Categoria};{veh.TipOp};{veh.TipBo};{veh.GetSubFormaPago()};{veh.InfoDac.Categoria};{vehiculo.NumeroTransito};{causaSimulacion.Codigo}");

            _estado = eEstadoVia.EVAbiertaLibre;
            _logicaVia.AdelantarVehiculo(eMovimiento.eOpCerrada);

            /*if (bajarBarrera)
            {
                Vehiculo oVehIng = _logicaVia.GetVehIng();
                bool bOcup = oVehIng.Ocupado;
                ulong numeroVehiculo = oVehIng.NumeroVehiculo;

                int nVeh = _logicaVia.LimpiarVehIng();
                
                _logicaVia.AdelantarFilaVehiculosDesde((eVehiculo)nVeh);
            }
            else
            {
                // Por si el vehiculo se movio
                if( ulNumVeh != _logicaVia.GetVehiculo( eVeh ).NumeroVehiculo )
                    eVeh = _logicaVia.BuscarVehiculo( ulNumVeh );

                _logicaVia.LimpiarVeh(eVeh);
                _logger.Debug("SimulacionPaso -> Limpiamos vehiculo {0}", eVeh.ToString());
            }*/

            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.Simulacion, 0);

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.CerrarCiclo();

            _logicaVia.ComitivaVehPendientes = 0;

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            _logicaVia.GrabarVehiculos();

            decimal vuelto = 0;
            int monto = 0;
            _logicaVia.GetPrimeroSegundoVehiculo().CobroEnCurso = false;
            _logicaVia.GetPrimerVehiculo().CobroEnCurso = false;
            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vuelto, ref listaDatosVia2);
            ClassUtiles.InsertarDatoVia(monto, ref listaDatosVia2);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_VUELTO, listaDatosVia2);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);



            _logger.Info("Simulacion Paso Fin");
        }

        #endregion

        #region DetraccionManual

        public override async void TeclaDetracManual(ComandoLogica comando)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            await ProcesarDetracManual(eTipoComando.eTecla, vehiculo);
        }

        /// <summary>
        /// Busca el valor de la detraccion y la asigna si corresponde para ese vehiculo
        /// </summary>
        private async Task ProcesarDetracManual(eTipoComando tipoComando, Vehiculo vehiculo)
        {
            if (ValidarPrecondicionesDetracManual(tipoComando))
            {
                if (vehiculo.AfectaDetraccion == 'S')
                {
                    TarifaABuscar buscardetraccion = GenerarTarifaABuscar(vehiculo.Categoria, 9);
                    Tarifa detraccion = await ModuloBaseDatos.Instance.BuscarTarifaAsync(buscardetraccion);
                    vehiculo.ValorDetraccion = detraccion.Valor;
                    vehiculo.EstadoDetraccion = 3;
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Detraccion");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Tarifa Total" + ": " + ClassUtiles.FormatearMonedaAString(vehiculo.Tarifa + vehiculo.ValorDetraccion));

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "No corresponde Detraccion");
                    vehiculo.EstadoDetraccion = 0;
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                    //ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia2);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
            }

        }

        private bool ValidarPrecondicionesDetracManual(eTipoComando tipoComando)
        {
            bool bRet = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                bRet = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    bRet = false;
                }


                else if (_logicaVia.EstaOcupadoSeparadorSalida())// _logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    bRet = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    bRet = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    bRet = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);

                    bRet = false;
                }
                else if (ModoPermite(ePermisosModos.PatenteObligatoria) && string.IsNullOrEmpty(vehiculo.Patente) && string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);

                    bRet = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                    bRet = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return bRet;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            bRet = false;
                            return bRet;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        bRet = false;
                    }
                }
                else if (vehiculo.InfoTag.GetTagHabilitado() && (vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Nada) && vehiculo.InfoTag.TipOp != 'C')
                //vehiculo.InfoTag.ErrorTag == eErrorTag.SinSaldo ||
                //vehiculo.InfoTag.TipBo == 'P')
                //vehiculo.TransitoUFRE)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TieneTag);

                    bRet = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();
                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    bRet = false;
                }
                else if (vehiculo.TipoVenta != eVentas.Nada)
                    bRet = false;
                /*else if( VerificarStatusImpresora() )
                {
                    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir(ClassUtiles.GetEnumDescr( errorImpresora )) );

                    retValue = false;
                }*/
            }



            return bRet;
        }

        #endregion

        #region Cancelacion

        /// <summary>
        /// Procesa la tecla CANCELAR enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaCancelar(ComandoLogica comando)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            bool bEsTagManual = vehiculo.InfoTag.TipOp == 'T' && vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre ? true : false;
            //si es tag manual pero el Tag se puede recargar, mostramos msj de cambio de forma de pago
            if (bEsTagManual)
                bEsTagManual = vehiculo.InfoTag.TagOK ? false : true;

            if (vehiculo.EsperaRecargaVia && ModoPermite(ePermisosModos.TagPagoViaOtrasFormas) && !bEsTagManual && vehiculo.InfoTag.TipOp != 'C' && vehiculo.EstaImpago)
            {
                Causa causa = new Causa(eCausas.CambiarFormaPago, ClassUtiles.GetEnumDescr(eCausas.CambiarFormaPago));

                List<DatoVia> listaDatosVia = new List<DatoVia>();

                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(comando, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
            }
            else if (vehiculo.EstaPagado && vehiculo.ListaRecarga.Where(x => x.Abortada != 'S').ToList().Any(x => x != null))
                CancelarRecarga(comando);
            else if (vehiculo.TipOp != 'C' && vehiculo.InfoTag.GetTagHabilitado() && vehiculo.EstaImpago)
                CancelarTagPago(comando);
            else
                await ProcesarCancelacion(eTipoComando.eTecla, null);
        }

        /// <summary>
        /// Procesa la cancelacion del transito, 
        /// se utiliza para las instancias de tecla presionada y de seleccion de causa
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="causaCancelacion"></param>
        private async Task ProcesarCancelacion(eTipoComando tipoComando, CausaCancelacion causaCancelacion)
        {
            if (ValidarPrecondicionesCancelacion(tipoComando))
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                if (tipoComando == eTipoComando.eTecla)
                {
                    List<CausaCancelacion> listaCausasCancelacion = await ModuloBaseDatos.Instance.BuscarCausasCancelacionAsync();

                    // Si hay al menos una causa de cancelacion
                    if (listaCausasCancelacion?.Count > 0)
                    {
                        ListadoOpciones opciones = new ListadoOpciones();

                        Causa causa = new Causa();
                        causa.Codigo = eCausas.CausaCancelacion;
                        causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.CausaCancelacion);

                        foreach (CausaCancelacion causas in listaCausasCancelacion)
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causas), true, causas.Descripcion, string.Empty, causas.Orden, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        // Envio la lista de causas de cancelacion a pantalla
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    //else
                    //{
                    //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de cancelacion" ));
                    //}

                }
                else if (tipoComando == eTipoComando.eSeleccion &&
                         causaCancelacion != null)
                {
                    Cancelacion(causaCancelacion);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda cancelar un transito
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesCancelacion(eTipoComando tipoComando)
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else if (vehiculo.TipOp == 'C' && !vehiculo.EstaPagado)
            {
                retValue = false;
            }
            else if (!vehiculo.EstaPagado || ImprimiendoTicket)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoNoPagado);

                retValue = false;
            }
            else if (_logicaVia.EstaOcupadoLazoSalida())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoCancelarSobreLazo);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Se cancela el transito, actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        /// <param name="causa"></param>
        public override void Cancelacion(CausaCancelacion causa, bool bAutomatico = false, ulong ulNumVeh = 0, eVehiculo eVeh = eVehiculo.eVehP1)
        {
            _logger?.Debug("Cancelación -> Inicio. Automatico? {0}", bAutomatico ? "SI" : "NO");

            //Limpiar info del vehiculo relacionada con el pago
            Vehiculo vehiculo;
            if (!bAutomatico)
                vehiculo = _logicaVia.GetVehTran();
            else
            {
                if (ulNumVeh > 0)
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(ulNumVeh));
                else
                    vehiculo = _logicaVia.GetVehiculo(eVeh);
            }

            string nroTag = vehiculo.InfoTag.NumeroTag;

            vehiculo.CancelacionEnCurso = true;

            if (vehiculo.EstaPagado && vehiculo.ListaRecarga.Where(x => x.Abortada != 'S').ToList().Any(x => x != null))
                ConfirmarCancelacionRecarga(ref vehiculo);

            _logger?.Debug("Transito abortado (NroTran[{0}] - NroVeh[{1}]). Causa: [{2}]", vehiculo.NumeroTransito, vehiculo.NumeroVehiculo, causa.Descripcion);
            _loggerTransitos?.Info($"K;{Fecha.ToString("HH:mm:ss.ff")};{vehiculo.Categoria};{vehiculo.TipOp};{vehiculo.TipBo};{vehiculo.GetSubFormaPago()};{vehiculo.InfoDac.Categoria};{vehiculo.NumeroTransito};{causa.Codigo}");

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            if (!bAutomatico)
            {
                // Se actualiza el estado de los perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                _logger?.Debug("Cancelacion -> BARRERA ABAJO!!");

                // Se actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Se envia mensaje al display
                ModuloDisplay.Instance.Enviar(eDisplay.BNV);
            }

            // Adelantar Vehiculo
            if (bAutomatico)
            {
                if (vehiculo.NumeroVehiculo > 0)
                    eVeh = _logicaVia.BuscarVehiculo(ulNumVeh);

                _logicaVia.GetVehiculo(eVeh).TipoObservacion = eTipoObservacion.Cancelacion;
                _logicaVia.GetVehiculo(eVeh).CodigoCancelacion = byte.Parse(causa.Codigo);
                _logicaVia.GetVehiculo(eVeh).Operacion = "AB";
                _logicaVia.GetVehiculo(eVeh).Abortada = true;
            }
            else
            {
                _logicaVia.GetVehTran().TipoObservacion = eTipoObservacion.Cancelacion;
                _logicaVia.GetVehTran().CodigoCancelacion = byte.Parse(causa.Codigo);
                _logicaVia.GetVehTran().Operacion = "AB";
                _logicaVia.GetVehTran().Abortada = true;

                //Actualizar estado vehiculo en pantalla
                ClassUtiles.InsertarDatoVia(_logicaVia.GetVehTran(), ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ACTUALIZA_ULTVEH, listaDatosVia);
            }

            //Capturo foto
            eCausaVideo causaVideo = eCausaVideo.Manual;
            _logicaVia.CapturaFoto(ref vehiculo, ref causaVideo);

            //Finalizo todos los videos pendientes
            ModuloVideo.Instance.DetenerVideo(vehiculo, eCausaVideo.Nada, eCamara.Lateral);
            int index = vehiculo.ListaInfoVideo.FindIndex(item => item.EstaFilmando == true);
            if (index != -1)
                vehiculo.ListaInfoVideo[index].EstaFilmando = false;
            _logicaVia.DecideAlmacenar(eAlmacenaMedio.Cancelada, ref vehiculo);

            if (bAutomatico)
                _logicaVia.OpAbortadaModoD(bAutomatico, eVeh, nroTag);
            else if (ModuloBaseDatos.Instance.ConfigVia.ModeloVia == "D")
                _logicaVia.OpAbortadaModoD();
            else
                _logicaVia.OpAbortadaEvento(ref vehiculo);

            if (!bAutomatico)
            {
                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;
                _logicaVia.AdelantarVehiculo(eMovimiento.eOpAbortada);

                vehiculo = _logicaVia.GetVehiculoAnterior();

                // Se envia mensaje a modulo de video continuo
                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.OperacionAbortada, null, null, vehiculo);

                // Se limpia el vehiculo y actualizo el numero de transito y factura
                vehiculo = new Vehiculo();
                vehiculo.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
                if (GetTurno.Mantenimiento == 'S')
                    vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketNoFiscal);
                else
                    vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                vehiculo.FormaPago = eFormaPago.Nada;
                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                _estado = eEstadoVia.EVAbiertaLibre;

                ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                // Update Online
                ModuloEventos.Instance.ActualizarTurno(_turno);
                UpdateOnline();
                _logicaVia.ComitivaVehPendientes = 0;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Transito Abortado");
            }

            vehiculo.CancelacionEnCurso = false;
            _logicaVia.GrabarVehiculos();

            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);

            _logger?.Debug("Cancelación -> Fin");
        }

        #endregion

        #region Tecla Escape
        /// <summary>
        /// Procesa la tecla ESCAPE enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaEscape(ComandoLogica comando)
        {
            if (_estado == eEstadoVia.EVAbiertaCat)
            {
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
            }
            if (comando.CodigoStatus == enmStatus.Abortada)
            {
                if (comando.Accion == enmAccion.LOGIN)
                {
                    if (_ultimaAccion.MismaAccion(enmAccion.T_TURNO))
                        _ultimaAccion.Clear();
                }
                else
                {
                    if (comando.Accion == enmAccion.T_ESCAPE && _bSIPforzado)
                    {
                        Causa causaRecibida = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);
                        if (causaRecibida.Codigo == eCausas.SimulacionPaso || causaRecibida.Codigo == eCausas.CausaSimulacion)
                            _bSIPforzado = false;
                    }
                    else if (comando.Accion == enmAccion.OPCION_MENU && _bSIPforzado)
                        _bSIPforzado = false;
                }
            }
            else
            {
                if (_ultimaAccion.AccionVencida())
                {
                    _logger?.Debug("TeclaEscape -> Clear Ultima Accion [{0}]", _ultimaAccion.AccionActual());
                    _ultimaAccion.Clear();
                }

                Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

                bool bEsTagManual = vehiculo.InfoTag.TipOp == 'T' && vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre ? true : false;
                //si es tag manual pero el Tag se puede recargar, mostramos msj de cambio de forma de pago
                if (bEsTagManual)
                    bEsTagManual = vehiculo.InfoTag.TagOK ? false : true;

                if (vehiculo.EsperaRecargaVia && ModoPermite(ePermisosModos.TagPagoViaOtrasFormas) && !bEsTagManual && vehiculo.InfoTag.TipOp != 'C' && vehiculo.EstaImpago)
                {
                    Causa causa = new Causa(eCausas.CambiarFormaPago, ClassUtiles.GetEnumDescr(eCausas.CambiarFormaPago));

                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(comando, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
                }
                else if (vehiculo.TipOp == 'C')
                    ClearChip(vehiculo);
            }
        }
        #endregion

        #region Exento

        /// <summary>
        /// Procesa la tecla EXENTO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async override void TeclaExento(ComandoLogica comando)
        {
            if (_turno.EstadoTurno == enmEstadoTurno.Quiebre)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EnQuiebre);
            else
            {
                Exento tipoExento = null;

                eTipoComando tipoComando = eTipoComando.eTecla;

                if (comando.Accion == enmAccion.T_EXENTOAMBU ||
                    comando.Accion == enmAccion.T_EXENTOBOMB ||
                    comando.Accion == enmAccion.T_EXENTOPOLI)
                {
                    tipoComando = eTipoComando.eExentoRapido;

                    tipoExento = new Exento();

                    // Se busca el tipo de exento
                    List<Exento> listaExento = await ModuloBaseDatos.Instance.BuscarListaExentosAsync((int)GetExentoRapido(comando.Accion));

                    if (listaExento != null && listaExento.Any())
                        tipoExento = listaExento[0];
                    else
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontro el tipo de exento"));
                }

                Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

                // Si ya tenia una patente y no es lectura de chip
                if (!string.IsNullOrEmpty(vehiculo.Patente) && vehiculo.InfoTag?.TipOp != 'C')
                {
                    PatenteExenta patenteExenta = new PatenteExenta();
                    patenteExenta.Patente = vehiculo.Patente;

                    await ProcesarExento(eTipoComando.eValidacion, eOrigenComando.Pantalla, tipoExento, patenteExenta);
                }
                else
                {
                    if (tipoComando == eTipoComando.eTecla && ModoPermite(ePermisosModos.ExentoSinPatente))
                        await ProcesarExento(eTipoComando.eSeleccion, eOrigenComando.Pantalla, tipoExento, null);
                    else
                    {
                        await ProcesarExento(eTipoComando.eExentoRapido, eOrigenComando.Pantalla, tipoExento, null);
                    }
                }
            }
        }

        private eExentosRapidos GetExentoRapido(enmAccion accion)
        {
            eExentosRapidos exentoRapido = eExentosRapidos.Ambulancia;

            switch (accion)
            {
                case enmAccion.T_EXENTOAMBU:
                    exentoRapido = eExentosRapidos.Ambulancia;
                    break;

                case enmAccion.T_EXENTOBOMB:
                    exentoRapido = eExentosRapidos.Bomberos;
                    break;

                case enmAccion.T_EXENTOPOLI:
                    exentoRapido = eExentosRapidos.Policia;
                    break;
            }

            return exentoRapido;
        }

        /// <summary>
        /// Recibe un JSON de pantalla con los datos necesarios para registrar un exento
        /// </summary>
        /// <param name="comando"></param>
        public override async void SalvarExento(ComandoLogica comando)
        {
            try
            {
                Opcion opcionSeleccionada = ClassUtiles.ExtraerObjetoJson<Opcion>(comando.Operacion);
                Exento tipoExento = JsonConvert.DeserializeObject<Exento>(opcionSeleccionada.Objeto);

                PatenteExenta patenteExento = ClassUtiles.ExtraerObjetoJson<PatenteExenta>(comando.Operacion);

                await ProcesarExento(eTipoComando.eConfirmacion, eOrigenComando.Pantalla, tipoExento, patenteExento);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa un transito exento, 
        /// se utiliza para las instancias de tecla presionada, validacion de patente, seleccion de exento y exento rapido
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <param name="tipoExento"></param>
        /// <param name="patente"></param>
        private async Task ProcesarExento(eTipoComando tipoComando, eOrigenComando origenComando, Exento tipoExento, PatenteExenta patenteExenta, Vehiculo vehiculoAux = null)
        {
            ModuloPantalla.Instance.LimpiarMensajes();
            Vehiculo vehiculo = new Vehiculo();

            if (await ValidarPrecondicionesExento(tipoComando, origenComando, tipoExento))
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    vehiculo.Patente = "";
                    await ProcesarPatente(eTipoComando.eTecla, vehiculo, eCausas.TipoExento, true);
                }
                else if (tipoComando == eTipoComando.ePatenteVacia)
                {
                    vehiculo.Patente = "";
                    await ProcesarPatente(eTipoComando.ePatenteVacia, vehiculo, eCausas.TipoExento, true);
                }
                else if ((tipoComando == eTipoComando.eValidacion ||
                            tipoComando == eTipoComando.eSeleccion) &&
                            origenComando != eOrigenComando.Supervision)
                {
                    PatenteExenta oExento = null;

                    if (vehiculoAux != null && !vehiculoAux.FormatoPatValido)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Formato de patente incorrecto"));
                    }
                    else
                    {
                        // Si tengo una patente
                        if (tipoComando == eTipoComando.eValidacion && !string.IsNullOrEmpty(patenteExenta?.Patente))
                        {
                            //Buscar la patente en la lista de Exentos Registrados
                            oExento = await ModuloBaseDatos.Instance.BuscarPatenteExentaAsync(patenteExenta.Patente.ToUpper());
                        }

                        if (oExento == null)
                            oExento = new PatenteExenta();

                        List<Exento> listaTipoExentos = await ModuloBaseDatos.Instance.BuscarListaExentosAsync();

                        // Encontro patente
                        if (!string.IsNullOrEmpty(oExento.Patente))
                        {
                            // Busco en la lista de tipos de exento a que exento se corresponde oExento
                            tipoExento = listaTipoExentos?.Find(item => item.Codigo == oExento.TipoExento);
                            await PagadoExento(oExento, tipoExento);
                        }
                        else
                        {
                            if (listaTipoExentos?.Count > 1)
                            {
                                ListadoOpciones opciones = new ListadoOpciones();

                                Causa causa = new Causa();
                                causa.Codigo = eCausas.TipoExento;
                                causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.TipoExento);

                                foreach (Exento exento in listaTipoExentos)
                                {
                                    CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(exento), true, exento.Descripcion, string.Empty, exento.Orden, false);
                                }

                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                                ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);
                                ClassUtiles.InsertarDatoVia(patenteExenta, ref listaDatosVia);

                                // Envio la lista de exentos
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.LIST_EXENTO, listaDatosVia);
                                // Mensaje de exento no registrdo
                                if (patenteExenta != null && !string.IsNullOrEmpty(patenteExenta.Patente))
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Exento no registrado") + $" ({patenteExenta.Patente.ToUpper()})");
                            }
                            else
                            {
                                if (listaTipoExentos != null && listaTipoExentos.Any())
                                {
                                    tipoExento = listaTipoExentos[0];
                                    await PagadoExento(oExento, tipoExento);
                                }
                                else
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron exentos"));
                                }
                            }
                        }
                    }
                }
                else if (tipoComando == eTipoComando.eConfirmacion ||
                         origenComando == eOrigenComando.Supervision)
                {
                    await PagadoExento(patenteExenta, tipoExento);
                }
                else if (tipoComando == eTipoComando.eExentoRapido)
                {
                    await PagadoExento(patenteExenta, tipoExento);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia2);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda habilitar el paso un vehiculo exento
        /// </summary>
        /// <param name="vehiculo"></param>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesExento(eTipoComando tipoComando, eOrigenComando origenComando, Exento tipoExento)
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.Exento))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    if (tipoComando == eTipoComando.eExentoRapido && tipoExento.SinCategorizar == 'S')
                    {
                        short monoc = (short)ModuloBaseDatos.Instance.ConfigVia.MonoCategoAutotab;
                        await Categorizar(monoc, false);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);
                        retValue = false;
                    }
                }
                else if (_logicaVia.EstaOcupadoSeparadorSalida()) //_logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();

                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }

                bool categoFPagoValida = await CategoriaFormaPagoValida('X', ' ', vehiculo.Categoria);

                if (!categoFPagoValida)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    retValue = false;
                }

                //Borrar datos del chip y limpiar mensajes
                if (retValue && vehiculo.InfoTag.TipOp == 'C' && !vehiculo.EstaPagado && vehiculo.ListaRecarga.Count() == 0)
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ClearChip(vehiculo);
                }
            }

            return retValue;
        }


        /// <summary>
        /// Se termina de procesar un transito exento, actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        /// <param name="tipoExento"></param>
        private async Task PagadoExento(PatenteExenta patenteExenta, Exento tipoExento)
        {
            _logger.Info("PagadoExento -> Inicio");
            InfoPagado oPagado;
            //Finaliza lectura de Tchip
            ModuloTarjetaChip.Instance.FinalizaLectura();
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
            vehiculo.CobroEnCurso = true;
            oPagado = vehiculo.InfoPagado;

            //Capturo foto y video
            _logicaVia.DecideCaptura(eCausaVideo.PagadoExento, vehiculo.NumeroVehiculo);

            // Se recalcula la tarifa
            if (oPagado == null || oPagado.Categoria == 0 || oPagado.Tarifa < 0 || oPagado.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
            {
                TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);
                Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                oPagado.Categoria = Convert.ToInt16(tarifa.CodCategoria);
                oPagado.Tarifa = tarifa.Valor;
                oPagado.DesCatego = tarifa.Descripcion;
                oPagado.CategoDescripcionLarga = tarifa.Descripcion;
                oPagado.TipoDiaHora = tarifa.CodigoHorario;
                oPagado.AfectaDetraccion = tarifa.AfectaDetraccion;
                if (vehiculo.AfectaDetraccion == 'S')
                {
                    TarifaABuscar buscardetraccion = GenerarTarifaABuscar(vehiculo.Categoria, 9);
                    Tarifa detraccion = await ModuloBaseDatos.Instance.BuscarTarifaAsync(buscardetraccion);
                    tarifa.ValorDetraccion = detraccion.Valor;
                    oPagado.ValorDetraccion = detraccion.Valor;
                }
                else
                    oPagado.ValorDetraccion = 0;
            }

            if (oPagado.Tarifa > 0)
            {
                _estado = eEstadoVia.EVAbiertaPag;

                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                oPagado.NoPermiteTag = true;
                oPagado.TipOp = 'X';
                oPagado.TipBo = ' ';
                oPagado.FormaPago = eFormaPago.CTExen;
                oPagado.Fecha = Fecha;
                oPagado.FechaFiscal = oPagado.Fecha;
                oPagado.CodExeVal = (byte)tipoExento.Codigo;
                oPagado.DescripcionCodExeVal = tipoExento.Descripcion;

                if (oPagado.InfoTag != null)
                {
                    oPagado.Patente = oPagado.InfoTag.Patente == oPagado.Patente ? "" : oPagado.Patente;
                    oPagado.InfoTag.Clear();
                    vehiculo.InfoTag.Clear();
                }

                // Es un exento sin tecla rapida, le corresponde una patente
                if (patenteExenta != null && !string.IsNullOrEmpty(patenteExenta.Patente))
                {
                    oPagado.Patente = patenteExenta.Patente;
                    oPagado.InfoTag.NumeroTag = patenteExenta.NumeroTag;
                }

                ModuloPantalla.Instance.LimpiarMensajes();

                //await GeneraClearing(oPagado, false );

                _loggerTransitos?.Info($"P;{oPagado.Fecha.ToString("HH:mm:ss.ff")};{oPagado.Categoria};{oPagado.TipOp};{oPagado.TipBo};{vehiculo.GetSubFormaPago()};{oPagado.Tarifa};{oPagado.NumeroTicketF};{oPagado.Patente};{oPagado.InfoTag.NumeroTag};{oPagado.InfoCliente.Ruc};0");

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                if (vehiculo.InfoOCRDelantero.Patente != "")
                    oPagado.InfoOCRDelantero = vehiculo.InfoOCRDelantero;
                vehiculo.CargarDatosPago(oPagado);
                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                {
                    ImprimiendoTicket = true;
                    errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Exentos, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia);
                    ImprimiendoTicket = false;
                }

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;

                //Se incrementa tránsito
                vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                // Se actualizan los perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                _logger?.Debug("PagadoExento -> BARRERA ARRIBA!!");

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                // Actualiza el mensaje en pantalla
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoExento);
                _logger?.Debug(eMensajesPantalla.PagadoExento.GetDescription());

                if (patenteExenta != null && !string.IsNullOrEmpty(patenteExenta.Patente))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Exento") + $": {tipoExento.Descripcion} - " + Traduccion.Traducir("Patente") + $": {patenteExenta.Patente}");
                    _logger?.Debug($"Exento: {tipoExento.Descripcion} - Patente: {patenteExenta.Patente}");
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Exento") + $": {tipoExento.Descripcion}");
                    _logger?.Debug($"Exento: {tipoExento.Descripcion}");
                }

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

                _logicaVia.IniciarTimerApagadoCampanaPantalla(2000);
                // Actualiza el estado de los mimicos en pantalla
                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Actualiza el estado de vehiculo en pantalla
                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                // Envia mensaje al display
                ModuloDisplay.Instance.Enviar(eDisplay.EXE, vehiculo);

                
                //almacena la foto
                ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                // Adelantar Vehiculo
                _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);


                //Vuelvo a buscar el vehiculo
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);

                ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Franquicia);

                //Vuelvo a buscar el vehiculo
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                //Se envía setCobro
                vehiculo.Operacion = "CB";
                ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                // Se envia mensaje a modulo de video continuo
                ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoExento, null, null, vehiculo);

                ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                // Update Online
                ModuloEventos.Instance.ActualizarTurno(_turno);
                UpdateOnline();

                _logicaVia.GrabarVehiculos();

            }
            else
            {
                vehiculo.CobroEnCurso = false;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TarifaErrorBusqueda);
                _logger.Info("PagadoExento -> Hubo un problema al consultar la tarifa");
            }

            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            _logicaVia.LoguearColaVehiculos();
        }

        #endregion

        #region Sube Barrera

        /// <summary>
        /// Procesa la tecla SUBE BARRERA enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaSubeBarrera(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Tecla)
            {
                // Se le manda null en la causa, ya que aun no se tiene
                await ProcesarSubeBarrera(eTipoComando.eTecla, eOrigenComando.Pantalla, null);
            }
        }

        /// <summary>
        /// Procesa la subida de barrera, 
        /// se utiliza para las instancias de tecla presionada y seleccion de causa
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <param name="causaApBarrera"></param>
        private async Task ProcesarSubeBarrera(eTipoComando tipoComando, eOrigenComando origenComando, CausaAperturaBarrera causaApBarrera)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesSubeBarrera(tipoComando, origenComando))
            {
                if (tipoComando == eTipoComando.eTecla && causaApBarrera == null)
                {
                    // Se consulta la lista de causas de apertura barrera al modulo de BD 
                    List<CausaAperturaBarrera> listaCausasApBarrera = await ModuloBaseDatos.Instance.BuscarCausasAperturaBarreraAsync();

                    if (listaCausasApBarrera?.Count > 0)
                    {
                        ListadoOpciones opciones = new ListadoOpciones();

                        Causa causa = new Causa();
                        causa.Codigo = eCausas.CausaSubeBarrera;
                        causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.CausaSubeBarrera);

                        foreach (CausaAperturaBarrera causas in listaCausasApBarrera)
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causas), true, causas.Descripcion, string.Empty, causas.Orden, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        // Envio la lista de causas de apertura de barrera a pantalla
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron causas de apertura de barrera"));
                    }
                }
                else if (tipoComando == eTipoComando.eSeleccion ||
                         origenComando == eOrigenComando.Supervision ||
                         causaApBarrera != null)
                {
                    SubeBarrera(causaApBarrera, origenComando);
                }
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda subir la barrera
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesSubeBarrera(eTipoComando tipoComando, eOrigenComando origenComando)
        {
            bool retValue = true;

            //if (_estado == eEstadoVia.EVCerrada)
            //{
            //    if (origenComando != eOrigenComando.Supervision)
            //    {
            //        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

            //        retValue = false;
            //    }
            //}
            if (!ModoPermite(ePermisosModos.AperturaBarrera))
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Barrera") + ": " + eMensajesPantalla.ModoNoPermiteEstaOperacion.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ModoNoPermiteEstaOperacion.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);

                retValue = false;
            }
            else if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Barrera") + ": " + eMensajesPantalla.BarreraEstabaAbierta.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.BarreraEstabaAbierta.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BarreraEstabaAbierta);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Se termina de procesar la subida de barrera, actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        /// <param name="causaApBarrera"></param>
        private void SubeBarrera(CausaAperturaBarrera causaApBarrera, eOrigenComando origenComando)
        {
            // Se actualizan perifericos
            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
            _logger?.Debug("SubeBarrera -> BARRERA ARRIBA!!");

            _logicaVia.GetVehIng().TipoObservacion = eTipoObservacion.AperturaBarrera;
            Vehiculo vehiculo = _logicaVia.GetVehIng();

            if (origenComando == eOrigenComando.Pantalla)
                vehiculo.ModoBarrera = 'T';
            else
                vehiculo.ModoBarrera = 'S';

            // Cambia el semaforo solo si el primer vehiculo esta pagado
            if (vehiculo.EstaPagado)
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);

            // Actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            int codApertura = 0;

            if (origenComando == eOrigenComando.Supervision)
                _turno.PcSupervision = 'S';

            if (causaApBarrera != null)
                int.TryParse(causaApBarrera.Codigo, out codApertura);

            // Se envia el evento de Apertura de Barrera
            ModuloEventos.Instance.SetAperturaBarrera(_turno, eModoBarrera.Apertura, codApertura);

            _turno.PcSupervision = 'N';

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.AperturaBarrera, null, null, null);

            if (origenComando == eOrigenComando.Pantalla && causaApBarrera != null)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Causa Sube Barrera") + ": " + causaApBarrera.Descripcion);
                _logger?.Debug("Causa Sube Barrera: " + causaApBarrera.Descripcion);
            }
            else
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Barrera") + ": OK");

                // Respuesta exitosa del comando al cliente grafico
                ResponderComandoSupervision("E");
                _logger?.Debug("Respuesta exitosa del comando al cliente grafico");

                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Causa Sube Barrera") + ": " + Traduccion.Traducir("Supervision"));
                _logger?.Debug("Causa Sube Barrera: Supervision");
            }

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            List<DatoVia> listaDatosVia3 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);

        }

        #endregion

        #region Baja Barrera

        /// <summary>
        /// Procesa la tecla BAJA BARRERA enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaBajaBarrera(ComandoLogica comando)
        {
            if (ValidarPrecondicionesBajaBarrera(eTipoComando.eTecla, eOrigenComando.Pantalla))
            {
                // Solicito confirmacion para bajar barrera
                Causa causa = new Causa(eCausas.BajarBarrera, ClassUtiles.GetEnumDescr(eCausas.BajarBarrera));
                List<DatoVia> listaDatosVia = new List<DatoVia>();

                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
            }
        }

        /// <summary>
        /// Procesa la bajada de barrera, 
        /// se utiliza para las instancias de tecla presionada y confirmacion
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        private void ProcesarBajaBarrera(eTipoComando tipoComando, eOrigenComando origenComando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (tipoComando == eTipoComando.eTecla ||
                origenComando == eOrigenComando.Supervision)
            {
                BajarBarrera();
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda bajar la barrera
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="origenComando"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesBajaBarrera(eTipoComando tipoComando, eOrigenComando origenComando)
        {
            bool retValue = true;

            if (!DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BarreraEstabaCerrada);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Se termina de procesar la bajada de barrera, actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        private void BajarBarrera()
        {
            // Se actualizan perifericos
            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

            // Actualiza el estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            // Se envia el evento de Cierre de Barrera
            ModuloEventos.Instance.SetAperturaBarrera(_turno, eModoBarrera.Cierre);

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.BajaBarrera, null, null, null);

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            //UpdateOnline();

            List<DatoVia> listaDatosVia3 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);

            _logger?.Debug("Bajada de barrera");
        }

        #endregion

        #region Semaforo Marquesina

        /// <summary>
        /// Procesa la tecla SEMAFORO MARQUESINA enviada desde pantalla
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaSemaforoMarquesina(ComandoLogica comando)
        {
            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
            //Si ya pasó mucho tiempo desde la última acción limpiamos
            if (!_ultimaAccion.MismaAccion(enmAccion.T_SEMAFORO) || _ultimaAccion.AccionVencida())
                _ultimaAccion.Clear();

            if (!_ultimaAccion.AccionEnProceso())
            {
                _logger.Debug("TeclaSemaforoMarquesina -> Accion Actual: {0}", enmAccion.T_SEMAFORO);
                _ultimaAccion.GuardarAccionActual(enmAccion.T_SEMAFORO);

                ProcesarSemaforoMarquesina(eEstadoSemaforo.Nada, eOrigenComando.Pantalla, eCausaSemaforoMarquesina.Tecla);
            }

        }

        /// <summary>
        /// Procesa las acciones necesarias para cambiar el semaforo de marquesina
        /// y envia el evento correspondiente
        /// </summary>
        /// <param name="color"></param>
        /// <param name="origen"></param>
        /// <param name="causaSemaforoMarquesina"></param>
        private void ProcesarSemaforoMarquesina(eEstadoSemaforo color, eOrigenComando origenComando, eCausaSemaforoMarquesina causaSemaforoMarquesina)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesSemaforoMarquesina(origenComando))
            {
                CambiarSemaforoMarquesina(color, origenComando, causaSemaforoMarquesina);

                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cambiar Semáforo de Marquesina") + ": OK");
                    ResponderComandoSupervision("E");
                }
            }
            _ultimaAccion.Clear();
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda cambiar el semaforo de marquesina
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesSemaforoMarquesina(eOrigenComando origenComando)
        {
            bool retValue = true;

            if (_estado == eEstadoVia.EVCerrada && !_esModoMantenimiento)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Cambiar Semáforo de Marquesina") + ": OK");
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaNoAbierta.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            return retValue;
        }

        /// <summary>
        /// Cambia el color del semaforo de marquesina dependiendo de los parametros
        /// </summary>
        /// <param name="color"></param>
        /// <param name="origen"></param>
        private void CambiarSemaforoMarquesina(eEstadoSemaforo color, eOrigenComando origen, eCausaSemaforoMarquesina causaSemaforoMarquesina)
        {
            Mimicos mimicos = new Mimicos();

            if (origen == eOrigenComando.Pantalla)
            {
                if (DAC_PlacaIO.Instance.ObtenerSemaforoMarquesina() == eEstadoSemaforo.Verde)
                    color = eEstadoSemaforo.Rojo;
                else if (DAC_PlacaIO.Instance.ObtenerSemaforoMarquesina() == eEstadoSemaforo.Rojo)
                    color = eEstadoSemaforo.Verde;

                DAC_PlacaIO.Instance.SemaforoMarquesina(color);
            }
            else if (origen == eOrigenComando.Supervision)
            {
                DAC_PlacaIO.Instance.SemaforoMarquesina(color);
            }

            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            // Se envia el evento de Semaforo de Marquesina
            EventoSemaforoMarquesina eventoSemaforoMarquesina = new EventoSemaforoMarquesina();
            eventoSemaforoMarquesina.Causa = ((int)causaSemaforoMarquesina).ToString()[0];

            eventoSemaforoMarquesina.Color = mimicos.SemaforoMarquesina == eEstadoSemaforo.Verde ? 'V' : 'R';

            eventoSemaforoMarquesina.Estado = _estado == eEstadoVia.EVCerrada ? 'C' : 'A';

            if (origen == eOrigenComando.Supervision)
                _turno.PcSupervision = 'S';

            ModuloEventos.Instance.SetSemaforoMarquesina(_turno, eventoSemaforoMarquesina);

            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Cambia Semáforo de Marquesina a") + " " + (mimicos.SemaforoMarquesina == eEstadoSemaforo.Verde ? Traduccion.Traducir("Verde") : Traduccion.Traducir("Rojo")));
            _logger?.Debug("Cambia semaforo de marquesina a " + (mimicos.SemaforoMarquesina == eEstadoSemaforo.Verde ? "Verde" : "Rojo"));
            _turno.PcSupervision = 'N';

            // Se envia mensaje a modulo de video continuo
            ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.CambioMarquesina, null, null, null, null, _estado == eEstadoVia.EVCerrada ? "C" : "A");

            // Alarma por semaforo en rojo con via abierta, cuando no es modo mantenimiento
            if (!_esModoMantenimiento)
            {
                if (color == eEstadoSemaforo.Rojo)
                    ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.ViaAbiertaSemRojo, 0);
                else
                    ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.ViaAbiertaSemRojo, 0);
            }

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();
        }

        #endregion

        #region Menu Retiro

        /// <summary>
        /// Procesa la tecla RETIRO enviada desde pantalla
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaRetiro(ComandoLogica comando)
        {
            await ProcesarTeclaRetiro(null);
        }

        /// <summary>
        /// Muestra un menu en pantalla con las opciones correspondientes al 
        /// menu retiro
        /// </summary>
        /// <param name="operador"></param>
        private async Task ProcesarTeclaRetiro(Operador operador)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesTeclaRetiro(operador))
            {
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ListadoOpciones opciones = new ListadoOpciones();

                bool mostrarOpcionesTeclaRetiro = true;

                Causa causa = new Causa(eCausas.Retiro, ClassUtiles.GetEnumDescr(eCausas.Retiro));

                int orden = 1;

                if (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat)
                {
                    if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
                        await ConsultarParte();

                    CargarOpcionMenu(ref opciones, eOpcionesTeclaRetiro.FondoCambio.ToString(), false, "Devolución Fondo de Cambio", string.Empty, orden++);
                    CargarOpcionMenu(ref opciones, eOpcionesTeclaRetiro.RetiroAnticipado.ToString(), false, "Retiro Anticipado", string.Empty, orden++);
                }
                else if (_estado == eEstadoVia.EVCerrada)
                {
                    if (operador != null)
                    {
                        SetOperadorActual(operador);

                        if (_turno?.Parte == null || _turno.Parte.NumeroParte == 0)
                            await ConsultarParte();

                        CargarOpcionMenu(ref opciones, eOpcionesTeclaRetiro.FondoCambio.ToString(), false, "Devolución Fondo de Cambio", string.Empty, orden++);
                        CargarOpcionMenu(ref opciones, eOpcionesTeclaRetiro.RetiroAnticipado.ToString(), false, "Retiro Anticipado", string.Empty, orden++);
                        CargarOpcionMenu(ref opciones, eOpcionesTeclaRetiro.LiquidacionFinal.ToString(), false, "Liquidación Final", string.Empty, orden++);
                    }
                    else
                    {
                        await ProcesarAperturaTurno(eTipoComando.eTecla, null, eOrigenComando.Pantalla, null, eCausas.Retiro);
                        mostrarOpcionesTeclaRetiro = false;
                    }
                }

                // Se envian las opciones de retiro a mostrar en pantalla
                if (mostrarOpcionesTeclaRetiro)
                {
                    ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Procesa la respuesta de pantalla, ejecutando alguno de los retiros correspondientes
        /// </summary>
        /// <param name="opcionTeclaRetiro"></param>
        private void ProcesarMenuTeclaRetiro(eOpcionesTeclaRetiro opcionTeclaRetiro)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            switch (opcionTeclaRetiro)
            {
                case eOpcionesTeclaRetiro.FondoCambio:
                    TeclaFondoDeCambio(null);
                    break;
                case eOpcionesTeclaRetiro.RetiroAnticipado:
                    TeclaRetiroAnticipado(null);
                    break;
                case eOpcionesTeclaRetiro.LiquidacionFinal:
                    TeclaLiquidacion(null);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para ejecutar el menu retiro
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesTeclaRetiro(Operador operador)
        {
            bool retValue = true;

            if (!IsNumeracionOk())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);

                retValue = false;
            }
            else if (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat || _estado == eEstadoVia.EVAbiertaPag)
            {
                Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

                if (_modo.Cajero == "N")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);

                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                    retValue = false;
                }
            }
            else if (_estado == eEstadoVia.EVCerrada && operador != null && _turno?.Parte?.NumeroParte == 0)
            {
                await ConsultarParte();

                if (_turno?.Parte?.NumeroParte == 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinParte);

                    retValue = false;
                }
            }

            return retValue;
        }

        #endregion

        #region Fondo de Cambio

        /// <summary>
        /// Procesa la tecla FONDO DE CAMBIO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaFondoDeCambio(ComandoLogica comando)
        {
            await ProcesarFondoDeCambio(null, "");
        }

        /// <summary>
        /// Recibe un JSON con los datos necesarios para salvar el fondo de cambio
        /// </summary>
        /// <param name="comando"></param>
        public override async void SalvarFondoDeCambio(ComandoLogica comando)
        {
            try
            {
                FondoCambio fondoCambio = ClassUtiles.ExtraerObjetoJson<FondoCambio>(comando.Operacion);
                await ProcesarFondoDeCambio(fondoCambio, "");
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa el fondo de cambio, 
        /// se utiliza para las instancias de seleccion y confirmacion
        /// </summary>
        /// <param name="tipoComando"></param>
        private async Task ProcesarFondoDeCambio(FondoCambio fondoCambio, string operadorId)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesFondoDeCambio())
            {
                if (fondoCambio == null)
                {
                    if (_ultimaAccion.MismaAccion(enmAccion.FONDO_CAMBIO))
                        _ultimaAccion.Clear();

                    // Si la via esta cerrada, y no tengo parte debo obtener los datos del mismo
                    if (_estado == eEstadoVia.EVCerrada && (_turno.Parte == null || _turno.Parte.NumeroParte == 0))
                    {
                        await ConsultarParte();
                    }

                    // Envia los datos para realizar el fondo de cambio a pantalla
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(_turno.Parte, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FONDO_CAMBIO, listaDatosVia);
                }
                else
                {
                    //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                    if (!_ultimaAccion.MismaAccion(enmAccion.FONDO_CAMBIO) || _ultimaAccion.AccionVencida())
                        _ultimaAccion.Clear();

                    if (VerificarStatusImpresora(false))
                    {
                        if (!_ultimaAccion.AccionEnProceso())
                        {
                            _logger.Debug("FondoDeCambio -> Accion Actual: {0}", enmAccion.FONDO_CAMBIO);
                            _ultimaAccion.GuardarAccionActual(enmAccion.FONDO_CAMBIO);
                            await DevolucionFondoDeCambio(fondoCambio);
                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
                        }
                    }
                    else
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda realizar la devolución de fondo de cambio
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesFondoDeCambio()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (!IsNumeracionOk())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);

                retValue = false;
            }
            else if (_estado == eEstadoVia.EVAbiertaLibre)
            {
                if (_modo.Cajero == "N")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);

                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                    retValue = false;
                }
            }
            else if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
            {
                await ConsultarParte();

                if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinParte);

                    retValue = false;
                }
            }

            if (retValue && _turno.Parte.Fondo == 'N')
                await ConsultarParte();

            if (retValue && _turno.Parte.Fondo == 'S')
            {
                await ConsultarParte();

                if (_turno.Parte.Fondo == 'S')
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FondoYaDevuelto);

                    retValue = false;
                }
            }
            else if (retValue && _turno.Parte.Fondo == ' ')
            {
                await ConsultarParte();

                if (_turno.Parte.Fondo == ' ')
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FondoNoEntregado);

                    retValue = false;
                }
            }

            return retValue;
        }

        /// <summary>
        /// Se termina de procesar la devolución de fondo de cambio, 
        /// actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        private async Task DevolucionFondoDeCambio(FondoCambio fondoCambio)
        {
            Vehiculo vehiculoImprimir = new Vehiculo();
            vehiculoImprimir.Fecha = DateTime.Now;

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                ImprimiendoTicket = true;

                errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(false, enmFormatoTicket.FondoCaja, vehiculoImprimir, _turno, ModuloBaseDatos.Instance.ConfigVia);

                ImprimiendoTicket = false;
            }

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel
            || ModoPermite(ePermisosModos.TransitoSinImpresora))
            {
                // Envia evento de Devolucion de Fondo de Cambio
                EnmStatusBD resultadoEvento = await ModuloEventos.Instance.SetFondoCambioAsync(_turno.Parte);

                if (resultadoEvento != EnmStatusBD.OK)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error en evento de fondo de cambio"));
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir(ClassUtiles.GetEnumDescr(eMensajesPantalla.FondoDevuelto)) + " (" + fondoCambio.SimboloMoneda + fondoCambio.Importe.ToString("0.00") + ")");
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);
                    _logger?.Debug(ClassUtiles.GetEnumDescr(eMensajesPantalla.FondoDevuelto) + " (" + fondoCambio.SimboloMoneda + fondoCambio.Importe.ToString("0.00") + ")");
                    _loggerTransitos?.Info($"F;{Fecha.ToString("HH:mm:ss.ff")};{fondoCambio.Importe};{fondoCambio.CajeroID};{fondoCambio.NumeroParte}");
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
                    // Actualiza el estado del fondo de cambio
                    //ParteBD parteBD = await ModuloBaseDatos.Instance.ObtenerParteAsync( _operadorActual.ID, _turno.FechaApertura );

                    //if( parteBD != null )
                    //    _turno.Parte.Fondo = parteBD.Fondo.GetValueOrDefault();
                }

                await ImprimirEncabezado();
            }
            else
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora"));
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir(errorImpresora.GetDescription()));
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
            }
        }

        #endregion

        #region Retiro Anticipado

        /// <summary>
        /// Procesa la tecla RETIRO ANTICIPADO enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaRetiroAnticipado(ComandoLogica comando)
        {
            await ProcesarRetiroAnticipado(null, null, "");
        }

        /// <summary>
        /// Salva la moneda seleccionada por el operador
        /// </summary>
        /// <param name="comando"></param>
        public override async void SalvarMoneda(ComandoLogica comando)
        {
            try
            {
                Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);

                Opcion opcionSeleccionada = ClassUtiles.ExtraerObjetoJson<Opcion>(comando.Operacion);
                Moneda moneda = JsonConvert.DeserializeObject<Moneda>(opcionSeleccionada.Objeto);

                if (causa.Codigo == eCausas.MonedaRetiro)
                {
                    //await ProcesarRetiroAnticipado( null, moneda, "" );
                    await ProcesarRetiroAnticipado(null, moneda, _operadorActual.ID);
                }
                else if (causa.Codigo == eCausas.Liquidacion)
                {

                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Salva el retiro anticipado, imprimiendo el comprobante 
        /// y generando el evento correspondiente
        /// </summary>
        /// <param name="comando"></param>
        public override async void SalvarRetiroAnticipado(ComandoLogica comando)
        {
            try
            {
                RetiroAnticipado retiroAnticipado = ClassUtiles.ExtraerObjetoJson<RetiroAnticipado>(comando.Operacion);
                Moneda moneda = ClassUtiles.ExtraerObjetoJson<Moneda>(comando.Operacion);

                await ProcesarRetiroAnticipado(retiroAnticipado, moneda, "");
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa el retiro anticipado, 
        /// se utiliza para las instancias de seleccion de moneda, 
        /// seleccion de monto y confirmacion
        /// </summary>
        /// <param name="tipoComando"></param>
        private async Task ProcesarRetiroAnticipado(RetiroAnticipado retiro, Moneda monedaSeleccionada, string operadorID)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesRetiroAnticipado())
            {
                if (monedaSeleccionada == null)
                {

                    // Se agrega este Clear, para el caso en que se hagan dos retiros seguidos, ya que el clear de RetiroAnticipado se quito
                    // para evitar dobles retiros si el cajero presiona ENTER muy seguido
                    if (_ultimaAccion.MismaAccion(enmAccion.RETIRO_ANT))
                        _ultimaAccion.Clear();

                    // Obtiene la lista de monedas de la base de datos
                    List<Moneda> listaMonedas = await ModuloBaseDatos.Instance.BuscarMonedasAsync();

                    // Si existe al menos una denominacion
                    if (listaMonedas?.Count() > 0)
                    {
                        Causa causa = new Causa(eCausas.MonedaRetiro, ClassUtiles.GetEnumDescr(eCausas.MonedaRetiro));

                        ListadoOpciones opciones = new ListadoOpciones();

                        int orden = 1;

                        foreach (Moneda item in listaMonedas)
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(item), true, item.Descripcion, string.Empty, orden++, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        // Envia lista de monedas a pantalla
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron Monedas"));
                    }
                }
                else if (retiro == null)
                {
                    // Si la via esta cerrada, debo obtener los datos del parte
                    if (_estado == eEstadoVia.EVCerrada && (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0))
                    {
                        await ConsultarParte();
                    }

                    if (_turno.Parte == null || _turno.Parte.NumeroParte == 0)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinParte);
                    }
                    else
                        retiro = new RetiroAnticipado();

                    if (ModoPermite(ePermisosModos.RetiroRegistraDenominacion))
                    {
                        List<Denominacion> listaDenominaciones = await ModuloBaseDatos.Instance.BuscarDenominacionAsync(monedaSeleccionada.Codigo);

                        if (listaDenominaciones != null && listaDenominaciones.Any())
                        {
                            ListadoDenominaciones listadoDenominaciones = new ListadoDenominaciones(listaDenominaciones);

                            retiro = new RetiroAnticipado();

                            Bolsa bolsa = new Bolsa();
                            bolsa.UsaBolsa = ModuloBaseDatos.Instance.ConfigVia.IngresaBolsa == "S";

                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(_turno.Parte, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(retiro, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(bolsa, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(monedaSeleccionada, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(listadoDenominaciones, ref listaDatosVia);

                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.RETIRO_ANT, listaDatosVia);
                        }
                        else
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron denominaciones"));
                        }
                    }
                    else
                    {
                        retiro.PorDenominacion = false;

                        Bolsa bolsa = new Bolsa();
                        bolsa.UsaBolsa = ModuloBaseDatos.Instance.ConfigVia.IngresaBolsa == "S";

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(_turno.Parte, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(retiro, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(bolsa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(monedaSeleccionada, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.RETIRO_ANT, listaDatosVia);
                    }
                }
                else
                {
                    retiro.Moneda = monedaSeleccionada;
                    retiro.ImporteConFormato = ClassUtiles.FormatearMonedaAString(retiro.Importe);

                    if (VerificarStatusImpresora(false))
                    {
                        //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                        if (!_ultimaAccion.MismaAccion(enmAccion.RETIRO_ANT) || _ultimaAccion.AccionVencida())
                            _ultimaAccion.Clear();

                        if (!_ultimaAccion.AccionEnProceso())
                        {
                            _logger.Debug("RetiroAnticipado -> Accion Actual: {0}", enmAccion.RETIRO_ANT);
                            _ultimaAccion.GuardarAccionActual(enmAccion.RETIRO_ANT);
                            await RetiroAnticipado(retiro);
                            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                        }
                    }
                    else
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }
        /// <summary>
        /// Valida las precondiciones para que se pueda realizar el retiro anticipado
        /// </summary>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesRetiroAnticipado()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVAbiertaLibre)
            {
                if (!IsNumeracionOk())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);

                    retValue = false;
                }
                else if (_modo.Cajero == "N")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);

                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                    retValue = false;
                }

                else if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
                {
                    await ConsultarParte();

                    if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinParte);

                        retValue = false;
                    }
                }

                if (retValue && _turno.Parte.Fondo == 'N' && !ModoPermite(ePermisosModos.RetiroSinDevolverFondo))  //No se devolvio el fondo de cambio
                {
                    await ConsultarParte();

                    if (_turno.Parte.Fondo == 'N')
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FondoAsignadoNoDevuelto);

                        retValue = false;
                    }
                }
            }
            else if (_estado == eEstadoVia.EVCerrada)
            {
                //Consulto el parte nuevamente
                await ConsultarParte();

                if (_turno.Parte?.Fondo == 'N' && !ModoPermite(ePermisosModos.RetiroSinDevolverFondo))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FondoAsignadoNoDevuelto);

                    retValue = false;
                }
            }

            return retValue;
        }

        /// <summary>
        /// Se termina de procesar el retiro anticipado, 
        /// actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        private async Task RetiroAnticipado(RetiroAnticipado retiro)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            Vehiculo vehRetiro = new Vehiculo();
            vehRetiro.CopiarVehiculo(ref vehiculo);

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                if (_estado == eEstadoVia.EVCerrada)
                {
                    _turno.NumeroVia = ModuloBaseDatos.Instance.ConfigVia.NumeroDeVia;
                    _turno.NumeroEstacion = ModuloBaseDatos.Instance.ConfigVia.NumeroDeEstacion;
                }
                vehRetiro.Fecha = Fecha;

                ImprimiendoTicket = true;

                errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(false, enmFormatoTicket.RetiroParcial, vehRetiro, _turno, ModuloBaseDatos.Instance.ConfigVia, retiro);

                ImprimiendoTicket = false;
            }

            if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                && !ModoPermite(ePermisosModos.TransitoSinImpresora))
            {
                //Indico el error de la impresora al usuario
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(errorImpresora.GetDescription()));
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
            }
            else
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);

                // Genera evento de Retiro Anticipado   
                retiro.Tipo = 'R';
                ModuloEventos.Instance.SetRetiro(_turno, retiro);
                //Mensaje Retiro Realizado
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir(ClassUtiles.GetDescription(eMensajesPantalla.RetiroRealizado)) + " (" + retiro.Moneda.SimboloMonetario + retiro.Importe.ToString("0.00") + ")");
                _logger?.Debug(ClassUtiles.GetDescription(eMensajesPantalla.RetiroRealizado) + " (" + retiro.Moneda.SimboloMonetario + retiro.Importe.ToString("0.00") + ")");
                _loggerTransitos?.Info($"J;{Fecha.ToString("HH:mm:ss.ff")};{retiro.Importe};{retiro.Moneda.Codigo};{retiro.Bolsa};{retiro.Precinto};{_turno.NumeroParte};{_turno.Operador.ID}");
                await ImprimirEncabezado();
            }


            // Se comenta, ya que si no se ejecuta mas de un retiro si el cajero teclea ENTER muy rapidamente
            //_ultimaAccion.Clear();
        }

        #endregion

        #region Liquidacion

        /// <summary>
        /// Procesa la tecla LIQUIDACION enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaLiquidacion(ComandoLogica comando)
        {
            await ProcesarLiquidacion(null);
        }

        /// <summary>
        /// Salva la liquidacion recibida de pantalla
        /// </summary>
        /// <param name="comando"></param>
        public async override void SalvarLiquidacion(ComandoLogica comando)
        {
            try
            {
                //ClassUtiles.ExtraerObjetoJson<_parte>( comando.Operacion );
                Liquidacion liquidacion = ClassUtiles.ExtraerObjetoJson<Liquidacion>(comando.Operacion);

                await ProcesarLiquidacion(liquidacion);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa la liquidacion y cierra el turno, 
        /// se utiliza para las instancias de seleccion y confirmacion
        /// </summary>
        /// <param name="tipoComando"></param>
        private async Task ProcesarLiquidacion(Liquidacion liquidacion)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesLiquidacion())
            {
                if (liquidacion == null)
                {
                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    List<Denominacion> listaDenominaciones = await ModuloBaseDatos.Instance.BuscarDenominacionAsync();

                    if (listaDenominaciones != null && listaDenominaciones.Any())
                    {
                        ListadoDenominaciones listadoDenominaciones = new ListadoDenominaciones(listaDenominaciones);

                        Bolsa bolsa = new Bolsa();
                        bolsa.UsaBolsa = ModuloBaseDatos.Instance.ConfigVia.IngresaBolsa == "S";

                        ClassUtiles.InsertarDatoVia(_turno.Parte, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(listadoDenominaciones, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(bolsa, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.LIQUIDACION, listaDatosVia);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron denominaciones"));
                    }
                }
                else
                {
                    //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                    if (!_ultimaAccion.MismaAccion(enmAccion.LIQUIDACION) || _ultimaAccion.AccionVencida())
                        _ultimaAccion.Clear();

                    if (!_ultimaAccion.AccionEnProceso())
                    {
                        _logger.Debug("ProcesarLiquidacion -> Accion Actual: {0}", enmAccion.LIQUIDACION);
                        _ultimaAccion.GuardarAccionActual(enmAccion.LIQUIDACION);

                        await Liquidacion(liquidacion);
                        await CierreTurno(false, eCodigoCierre.LiquidacionFinal, eOrigenComando.Pantalla);
                    }
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }

        }

        /// <summary>
        /// Validar precondiciones necesarias para poder realizar una Liquidacion.
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesLiquidacion()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (!IsNumeracionOk())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);

                retValue = false;
            }
            else if (_estado == eEstadoVia.EVAbiertaLibre)
            {
                if (_modo.Cajero == "N")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);

                    retValue = false;
                }

                List<CausaCierre> listaCausasCierre = await ModuloBaseDatos.Instance.BuscarCausasCierreAsync();

                if (retValue && listaCausasCierre != null && !listaCausasCierre.Any(x => x.ConLiquidacion == "S"))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoLiquidacion);

                    retValue = false;
                }
                else if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);

                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                    retValue = false;
                }
            }
            else if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
            {
                //Consulto el parte nuevamente
                await ConsultarParte();

                if (_turno?.Parte == null || _turno?.Parte.NumeroParte == 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinParte);

                    retValue = false;
                }
            }

            if (retValue && _turno.Parte.Fondo == 'N')
            {
                //Consulto el parte nuevamente
                await ConsultarParte();

                _logger.Debug("ValidarPrecondicionesLiquidacion -> Fondo: {0}", _turno.Parte.Fondo);

                if (!ModoPermite(ePermisosModos.LiquidacionSinDevolverFondo) && _turno.Parte.Fondo == 'N')
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FondoAsignadoNoDevuelto);

                    retValue = false;
                }
            }

            return retValue;
        }

        /// <summary>
        /// Genera el evento de liquidacion correspondiente e imprime el ticket
        /// </summary>
        /// <param name="liquidacion"></param>
        private async Task Liquidacion(Liquidacion liquidacion)
        {
            _logger?.Info("Liquidacion -> Inicio");

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                ImprimiendoTicket = true;

                RetiroAnticipado retiro = new RetiroAnticipado();

                retiro.Importe = liquidacion.Importe;
                retiro.ListaLiquidacionxDenominacion = liquidacion.ListaLiquidacionxDenominacion;
                retiro.Bolsa = liquidacion.NumeroBolsa;
                retiro.Observacion = liquidacion.Observacion;

                Vehiculo vehiculo = new Vehiculo();
                vehiculo.Fecha = Fecha;

                errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(false, enmFormatoTicket.Liquidacion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, retiro);

                ImprimiendoTicket = false;
            }

            if ((errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
             || ModoPermite(ePermisosModos.TransitoSinImpresora))
            {
                EnmStatusBD respuestaEvento = await ModuloEventos.Instance.SetLiquidacionXMLAsync(_turno, liquidacion);

                if (respuestaEvento == EnmStatusBD.OK)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Liquidación realizada por: ") + ClassUtiles.FormatearMonedaAString(liquidacion.Importe));
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);
                    _logger?.Info("Liquidacion realizada por " + ClassUtiles.FormatearMonedaAString(liquidacion.Importe));
                    _loggerTransitos?.Info($"L;{Fecha.ToString("HH:mm:ss.ff")};{liquidacion.Importe};{liquidacion.NumeroBolsa};{liquidacion.NumeroPrecinto};{_turno.NumeroParte};{_turno.Operador.ID}");

                    await ImprimirEncabezado();
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No pudo realizarse la liquidación"));
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }
            else
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                //Si no se permite la impresion, muestro mensaje
                //if (!ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    // ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                }
                //else
                {
                    //Indico el error de la impresora al usuario
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora"));
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir(errorImpresora.GetDescription()));
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                }
            }

            _logger?.Info("Liquidacion -> Fin");
        }

        #endregion

        #region Patente

        /// <summary>
        /// Procesa la tecla PATENTE enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaPatente(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Tecla)
            {
                Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo();
                await ProcesarPatente(eTipoComando.eTecla, vehiculo, eCausas.IngresoPatente, true);
            }
        }

        /// <summary>
        /// Procesa la patente ingresada e intenta validarla
        /// </summary>
        /// <param name="comando"></param>
        public override async void ProcesarPatenteIngresada(ComandoLogica comando)
        {
            //ModuloPantalla.Instance.LimpiarMensajes();

            try
            {
                if (comando.CodigoStatus == enmStatus.Ok)
                {
                    //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                    if (!_ultimaAccion.MismaAccion(enmAccion.TRA_PATENTE) || _ultimaAccion.AccionVencida())
                        _ultimaAccion.Clear();

                    if (!_ultimaAccion.AccionEnProceso())
                    {
                        _logger.Debug("PatenteIngresada -> Accion Actual: {0}", enmAccion.TRA_PATENTE);
                        _ultimaAccion.GuardarAccionActual(enmAccion.TRA_PATENTE);

                        Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);
                        Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);
                        await ProcesarPatente(eTipoComando.eValidacion, vehiculo, causa.Codigo, true, false);
                    }
                    else
                        _logger.Debug("PatenteIngresada -> PROCESANDO Accion: {0}", enmAccion.TRA_PATENTE);
                }
                else if (comando.CodigoStatus == enmStatus.Abortada)
                {
                    _logicaVia.GetPrimerVehiculo().EnOperacionManual = false;
                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa la patente, se utiliza para las instancias de tecla y validacion
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="patente"></param>
        private async Task ProcesarPatente(eTipoComando tipoComando, Vehiculo vehiculo, eCausas eCausa, bool tomarFoto, bool limpiarMensajes = true)
        {
            if (limpiarMensajes)
                ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesPatente())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    Causa causa = new Causa();

                    causa.Codigo = eCausa;
                    causa.Descripcion = Traduccion.Traducir(ClassUtiles.GetEnumDescr(eCausa));

                    Vehiculo veh = new Vehiculo();
                    veh.Patente = vehiculo?.Patente;
                    veh.InfoOCRDelantero = vehiculo?.InfoOCRDelantero;
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(veh, ref listaDatosVia);

                    vehiculo.EnOperacionManual = true;

                    // Se pide el ingreso de patente
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_PATENTE, listaDatosVia);
                    

                    // Se toma una foto y se envia el path a pantalla para mostrarla
                    if (tomarFoto)
                        ProcesarFoto(false);
                }
                else if (tipoComando == eTipoComando.eValidacion || tipoComando == eTipoComando.ePatenteVacia)
                {
                    if (vehiculo.FormatoPatValido && (eCausa == eCausas.Nada || eCausa == eCausas.IngresoPatente))
                        AsignarPatente(vehiculo.Patente);
                    else if (eCausa == eCausas.TipoExento)
                    {
                        if (!ModoPermite(ePermisosModos.ExentoPatenteVacia) && string.IsNullOrEmpty(vehiculo.Patente))
                        {
                            await ProcesarExento(eTipoComando.eTecla, eOrigenComando.Pantalla, null, null);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.IngresePatente);
                        }
                        else
                        {
                            PatenteExenta patenteExenta = new PatenteExenta();
                            patenteExenta.Patente = vehiculo.Patente;
                            await ProcesarExento(eTipoComando.eValidacion, eOrigenComando.Pantalla, null, patenteExenta, vehiculo);
                        }
                    }
                    else if (eCausa == eCausas.TagManual && !string.IsNullOrEmpty(vehiculo.Patente))
                        await ProcesarTagManual(vehiculo.Patente, null, eOrigenComando.Pantalla, vehiculo.FormatoPatValido);
                    else if (eCausa == eCausas.CobroDeuda)
                    {
                        AsignarPatente(vehiculo.Patente);
                        await ProcesarCobroDeuda(eTipoComando.eValidacion, vehiculo);
                    }
                    else if (eCausa == eCausas.CobroDeuda)
                    {
                        AsignarPatente(vehiculo.Patente);
                        await ProcesarPagoDiferido(eTipoComando.eValidacion, vehiculo);
                    }
                    else if (!vehiculo.FormatoPatValido)
                        ProcesarPatente(eTipoComando.eTecla, vehiculo, eCausa, true);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
            }

            _ultimaAccion.Clear();
        }

        /// <summary>
        /// Valida las precondiciones para que se pueda asignar una patente a un vehiculo
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="patente"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesPatente()
        {
            bool esPrimero = false;
            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);

            bool retValue = true;

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else if (EsCambioJornada())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Se asigna la patente al transito, 
        /// actualizando estado en pantalla y enviando los eventos correspondientes
        /// </summary>
        /// <param name="patente"></param>
        private void AsignarPatente(string patente)
        {
            bool esPrimero = false;
            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);

            if(vehiculo.EstaPagado == true)
            {
                _logicaVia.GetVehIng().Patente = patente;
            }
            else
                vehiculo.Patente = patente;

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

            // Muestro la patente ingresada en pantalla
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Patente") + ": " + patente.ToUpper());

            _logger?.Debug("Patente asignada " + patente.ToUpper());

            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            else
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
        }

        #endregion

        #region Foto

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA FOTO
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaFoto(ComandoLogica comando)
        {
            if (string.IsNullOrEmpty(comando.Operacion))
            {
                ProcesarFoto();
            }
            else
            {
                SolicitudNuevaFoto nuevaFoto = Utiles.ClassUtiles.ExtraerObjetoJson<SolicitudNuevaFoto>(comando.Operacion);

                ProcesarFoto(nuevaFoto.NuevaFoto);
            }
        }

        /// <summary>
        /// Toma una foto y envia el path de la misma al modulo de pantalla
        /// </summary>
        private void ProcesarFoto(bool limpiarMensajes = true)
        {
            if (ValidarPrecondicionesTeclaFoto())
            {
                try
                {
                    _logger?.Trace("Inicio");

                    if (limpiarMensajes)
                        ModuloPantalla.Instance.LimpiarMensajes();

                    bool esPrepago = false;
                    Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrepago);

                    eCausaVideo causaVideo = eCausaVideo.Manual;

                    _logicaVia.CapturaFoto(ref vehiculo, ref causaVideo, true);

                    int index = vehiculo.ListaInfoFoto.FindIndex(ind => ind.EsManual == true);

                    InfoMedios infoMedios;
                    if (index >= 0)
                    {
                        infoMedios = vehiculo.ListaInfoFoto[vehiculo.ListaInfoFoto.FindIndex(ind => ind.EsManual == true)];

                        Foto fotoManual = new Foto();

                        fotoManual.PathFoto = ClassUtiles.LeerConfiguracion("MODULO_FOTO", "PATH_FOTO");

                        //fotoManual.PathFoto = "C:\\VIDEO\\AVI\\";
                        fotoManual.Nombre = infoMedios.NombreMedio;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(fotoManual, ref listaDatosVia);

                        _logger?.Trace("Foto[{0}] Path[{1}]", fotoManual.Nombre, fotoManual.PathFoto);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.FOTO, listaDatosVia);
                    }
                    else
                    {
                        _logger?.Info("Error al capturar foto");
                    }
                }
                catch (Exception e)
                {
                    _loggerExcepciones.Error(e);
                }
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder sacar una foto
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesTeclaFoto()
        {
            bool retValue = true;

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else
            {

            }

            return retValue;
        }
        #endregion

        #region Menú

        /// <summary>
        /// Procesa la tecla MENU enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaMenu(ComandoLogica comando)
        {
            await ProcesarMenu(eTipoComando.eTecla, null);
        }

        /// <summary>
        /// Envia la lista de opciones a pantalla, 
        /// y se encarga luego de procesar la opcion seleccionada
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="opcionSeleccionada"></param>
        private async Task ProcesarMenu(eTipoComando tipoComando, Opcion opcionSeleccionada)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (tipoComando == eTipoComando.eTecla)
            {
                // Carga la lista de opciones de acuerdo al estado actual de la via
                ListadoOpciones opcionesMenu = await CargarOpcionesTeclaMenu();

                Causa causa = new Causa();
                causa.Codigo = eCausas.Menu;
                causa.Descripcion = Traduccion.Traducir(ClassUtiles.GetEnumDescr(eCausas.Menu));

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(opcionesMenu, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                // Envia la lista de opciones de menu a pantalla
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_MENU, listaDatosVia);
            }
            else if (tipoComando == eTipoComando.eSeleccion)
            {
                eOpcionMenu opcionMenu;

                if (!Enum.TryParse(opcionSeleccionada.Objeto, out opcionMenu))
                {
                    _logger?.Warn("Error al parsear opcion menu");
                }
                else
                {
                    switch (opcionMenu)
                    {
                        case eOpcionMenu.MensajesSupervision:
                            TeclaMensajeASupervision(null);
                            break;

                        case eOpcionMenu.VideoInterno:
                            TeclaVideoInterno(null);
                            break;

                        case eOpcionMenu.IniciarQuiebre:
                            TeclaInicioQuiebre(null);
                            break;

                        case eOpcionMenu.FinalizarQuiebre:
                            TeclaFinQuiebre(null);
                            break;

                        case eOpcionMenu.Observaciones:
                        case eOpcionMenu.ObservacionesTransitoAnterior:
                            TeclaObservacion(opcionMenu);
                            break;
                        case eOpcionMenu.ObservacionesViolacion:
                            TeclaObservacion(opcionMenu);
                            break;

                        case eOpcionMenu.AbrirModoAutomatico:
                            await ProcesarAperturaAutomatica(null, eOrigenComando.Pantalla);
                            break;

                        case eOpcionMenu.ImpresionEncabezado:
                            await ProcesarOpcionImpresora(eOpcionesImpresora.ReimprimirEncabezado);
                            break;

                        case eOpcionMenu.StatusImpresora:
                            await ProcesarOpcionImpresora(eOpcionesImpresora.Status);
                            break;

                        case eOpcionMenu.Tagmanual:
                            TeclaTagManual(null);
                            break;

                        case eOpcionMenu.Retiro:
                            if (_estado == eEstadoVia.EVCerrada)
                                await ProcesarTeclaRetiro(null);
                            else
                                await ProcesarTeclaRetiro(_operadorActual);
                            break;

                        case eOpcionMenu.AutorizarNumeracion:
                            ProcesarNumeracion(_operadorActual);
                            break;

                        case eOpcionMenu.ConsultarNumeracion:
                            ConsultarNumeracion(_operadorActual);
                            break;

                        case eOpcionMenu.ReimprimirTicket:
                            ProcesarReimprimirTicket(opcionMenu);
                            break;


                        case eOpcionMenu.Detraccion:
                            TeclaDetraccion(opcionMenu);
                            break;

                        case eOpcionMenu.ValePrepago:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "TECLA VALE PREPAGO NO IMPLEMENTADA");
                            break;

                        case eOpcionMenu.CierreZ:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "PANAVIAL NO IMPLEMENTA TECLA CIERREZ");
                            break;

                        case eOpcionMenu.ComitivaEfectivo:
                            await ProcesarComitiva(eTipoComando.eTecla, null, new Causa { Codigo = eCausas.ComitivaEfectivo });
                            break;

                        case eOpcionMenu.ComitivaExento:
                            await ProcesarComitiva(eTipoComando.eTecla, null, new Causa { Codigo = eCausas.ComitivaExento });
                            break;

                        case eOpcionMenu.CobroDeuda:
                            RecibirCobroDeuda(null);
                            break;

                        case eOpcionMenu.Alarma:
                            RecibirAlarma(null);
                            break;

                        case eOpcionMenu.ConsultaDeuda:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "PANAVIAL NO IMPLEMENTA TECLA CONSULTA DEUDA");
                            break;

                        case eOpcionMenu.GenerarPagoDiferido:
                            GenerarPagoDiferido(null);
                            break;

                        case eOpcionMenu.TeclaVuelto:
                            ProcesarVuelto(null);
                            break;

                        case eOpcionMenu.TicketManual:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "PANAVIAL NO IMPLEMENTA TECLA TICKET MANUAL");
                            break;

                        case eOpcionMenu.VerOcultarColaVehiculos:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "PANAVIAL NO IMPLEMENTA TECLA VER/OCULTAR COLA VEHICULOS");
                            break;

                        case eOpcionMenu.AbrirCerrarBarreraEscape:
                            AbrirCerrarBarreraEscape(false);
                            break;

                        default:
                            _logger?.Warn("Opcion menu no encontrada");
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Carga las opciones a mostrar en el menu de acuerdo a 
        /// las condiciones para mostrar de cada una
        /// </summary>
        private async Task<ListadoOpciones> CargarOpcionesTeclaMenu()
        {
            ListadoOpciones opcionesMenu = new ListadoOpciones();
            //TODO ver cada caso
            bool esPrimero = false;
            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);

            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.MensajesSupervision.ToString(), false, "Mensajes a Supervisión", string.Empty, 1);
            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.VideoInterno.ToString(), false, "Video y Audio Interno", string.Empty, 15);
            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Alarma.ToString(), true, "Alarma", string.Empty, 13);
            //_estadoNumeracion = eEstadoNumeracion.SinNumeracion;

            // Va solo en el menu del tecnico
            if (_estadoNumeracion != eEstadoNumeracion.NumeracionOk && _operadorActual != null && int.Parse(_operadorActual?.NivelAcceso) == (int)eNivelUsuario.Tecnico)
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.AutorizarNumeracion.ToString(), false, "Autorizar Numeración", string.Empty, 8);

            // Va solo en el menu del tecnico
            int n = 0;
            int.TryParse(_operadorActual?.NivelAcceso, out n);
            if (_estadoNumeracion == eEstadoNumeracion.NumeracionOk && _operadorActual != null && n == (int)eNivelUsuario.Tecnico)
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ConsultarNumeracion.ToString(), false, "Consultar Numeración", string.Empty, 8);

            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.StatusImpresora.ToString(), false, "Status de Impresora", string.Empty, 2);

            if (_estado == eEstadoVia.EVCerrada)
            {
                // A la espera de un cliente con impresora fiscal
                // Si tiene numero de punto de venta, es impresora fiscal
                //if( ModuloBaseDatos.Instance.ConfigVia.NumeroPuntoVta != 0 )
                //    CargarOpcionMenu( ref opcionesMenu, eOpcionMenu.CierreZ.ToString(), true, "Cierre Z", orden++);

                List<Modos> listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);

                if (listaModos?.Any(x => x.Cajero == "N") ?? false)
                {
                    if (!DAC_PlacaIO.Instance.EntradaBidi())
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.AbrirModoAutomatico.ToString(), true, "Apertura Modo Automático", string.Empty, 3);
                }

                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.IniciarQuiebre.ToString(), false, "Iniciar Quiebre", "Quiebre", 8);
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Retiro.ToString(), false, "Retiro", "Retiro", 7);
            }
            else if (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat)
            {
                if (_modo.Cajero == "S")
                {
                    if (!vehiculo.EstaPagado)
                    {
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ImpresionEncabezado.ToString(), true, "Impresión Encabezado", string.Empty, 5);
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Retiro.ToString(), false, "Retiro", "Retiro", 7);
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ComitivaEfectivo.ToString(), true, "Comitiva EFECTIVO", string.Empty, 10);
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ComitivaExento.ToString(), true, "Comitiva EXENTO", string.Empty, 11);

                        

                        if (vehiculo.Categoria > 0)
                        {
                            bool categoFPagoValida = await CategoriaFormaPagoValida('T');

                            if (categoFPagoValida)
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Tagmanual.ToString(), false, "Placa Manual", "TagManual", 6);

                            categoFPagoValida = await CategoriaFormaPagoValida('V');

                            if (ModoPermite(ePermisosModos.ValePrepago) && categoFPagoValida)
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ValePrepago.ToString(), true, "Vale Prepago", "ValePrepago", 4);

                            categoFPagoValida = await CategoriaFormaPagoValida('D');

                            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.TeclaVuelto.ToString(), true, "Vuelto", "Vuelto", 16);

                            if (ModoPermite(ePermisosModos.PagoDiferido) && categoFPagoValida)
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.GenerarPagoDiferido.ToString(), true, "Pago Diferido", "PagoDiferido", 7);

                            if (ModuloBaseDatos.Instance.ConfigVia.LeeClearing == "S")
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.AutorizacionPaso.ToString(), true, "Autorización de Paso Manual", "AutorizacionPaso", 3);

                            if (ModoPermite(ePermisosModos.TicketManual))
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.TicketManual.ToString(), true, "Ticket Manual", string.Empty, 12);

                            if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Detraccion.ToString(), true, "Detraccion", string.Empty, 14, false);
                        }
                    }
                    // Primer vehiculo esta pagado
                    else
                    {                        
                        if (_estado == eEstadoVia.EVAbiertaCat)
                            CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Observaciones.ToString(), false, "Observaciones", "Observaciones", 4);
                    }

                }
                // Si es modo sin cajero
                else
                {
                    if (_modo.Modo == "R")
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.VerOcultarColaVehiculos.ToString(), true, "Ver/Ocultar cola de vehículos", string.Empty, 7);
                }

                ulong vehiculosDesdeApertura = 0;

                vehiculosDesdeApertura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito) -
                                         ModuloBaseDatos.Instance.BuscarValorInicialContador(eContadores.NumeroTransito);
                if (vehiculosDesdeApertura >= 0)
                {
                    if (_estado == eEstadoVia.EVAbiertaCat)
                        CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Observaciones.ToString(), false, "Observaciones", "Observaciones", 4);
                    else
                    {
                        if (_logicaVia.GetVehObservado().NoVacio)
                        {
                            if (_logicaVia.GetVehObservado().Operacion == "VI")
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ObservacionesViolacion.ToString(), false, "Observaciones Violación", "Observaciones", 4);
                            else
                                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ObservacionesTransitoAnterior.ToString(), false, "Observaciones Tránsito Anterior", "Observaciones", 4);
                        }
                    }
                }

                if (!DAC_PlacaIO.Instance.EntradaBidi())
                    CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.IniciarQuiebre.ToString(), false, "Iniciar Quiebre", "Quiebre", 8);
            }
            else if (_estado == eEstadoVia.EVQuiebreBarrera)
            {
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.FinalizarQuiebre.ToString(), true, "Terminar Quiebre", "Quiebre", 8);
            }
            else if (_estado == eEstadoVia.EVAbiertaPag)
            {
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.Observaciones.ToString(), false, "Observaciones", "Observaciones", 4);
                CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ReimprimirTicket.ToString(), false, "Reimprimir Ticket", string.Empty, 18, false);
            }
            else if (!vehiculo.EstaPagado)
            {

                bool categoFPagoValida = await CategoriaFormaPagoValida('D');

                if (categoFPagoValida)
                    CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.ConsultaDeuda.ToString(), true, "Consulta de Deuda", string.Empty, 11);
            }
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape() && ModoPermite(ePermisosModos.MenuBarreraViaEscape))
            {
                if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Escape))
                    CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.AbrirCerrarBarreraEscape.ToString(), false, "Cerrar Barrera Escape", "Quiebre", 9);
                else
                    CargarOpcionMenu(ref opcionesMenu, eOpcionMenu.AbrirCerrarBarreraEscape.ToString(), false, "Abrir Barrera Escape", "Quiebre", 9);
            }

            return opcionesMenu;
        }

        #endregion

        #region Barrera Via de escape
        private void AbrirCerrarBarreraEscape(bool desdeSupervision)
        {
            _logicaVia.PulsadorEscape(0, desdeSupervision);

        }
        #endregion

        #region Inicio Quiebre Liberado

        /// <summary>
        /// Procesa la tecla INICIAR QUIEBRE enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaInicioQuiebre(ComandoLogica comando)
        {
            await ProcesarInicioQuiebre(null, eOrigenComando.Pantalla);
        }

        /// <summary>
        /// Inicia el quiebre liberado, realizando la apertura de turno 
        /// y enviando los eventos correspondientes
        /// </summary>
        /// <param name="operador"></param>
        /// <param name="origenComando"></param>
        private async Task ProcesarInicioQuiebre(Operador operador, eOrigenComando origenComando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesInicioQuiebre(operador, origenComando))
            {
                if (operador == null)
                {
                    Runner runner = await ModuloBaseDatos.Instance.BuscarRunnerActualAsync();

                    if (runner == null)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se pudo obtener runner actual"));
                    else
                    {
                        Operador nuevoOperador = await ModuloBaseDatos.Instance.BuscarOperadorAsync(runner.IDSupervisor);
                        await ProcesarAperturaTurno(eTipoComando.eTecla, null, eOrigenComando.Pantalla, nuevoOperador, eCausas.Quiebre);
                    }
                }
                else
                {
                    // Nuevo bloque a cargo del supervisor
                    if (_estado != eEstadoVia.EVCerrada)
                        await CierreTurno(true, eCodigoCierre.QuiebreLiberado, origenComando);

                    _turno.Operador = operador;
                    SetOperadorActual(operador);

                    await InicioQuiebre(origenComando);

                    if (origenComando == eOrigenComando.Supervision)
                        ResponderComandoSupervision("E");
                }
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder realizar un inicio de quiebre
        /// </summary>
        /// <param name="operador"></param>
        /// <returns></returns>
        private bool ValidarPrecondicionesInicioQuiebre(Operador operador, eOrigenComando origenComando)
        {
            bool retValue = true;

            if (_estado != eEstadoVia.EVAbiertaLibre && DAC_PlacaIO.Instance.EntradaBidi())
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": " + eMensajesPantalla.ViaOpuestaAbierta.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaOpuestaAbierta.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaOpuestaAbierta);

                retValue = false;
            }
            else if (_logicaVia.GetHayVehiculosPagados())
            {
                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", eMensajesPantalla.ExistenVehiculosPagados.GetDescription());
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ExistenVehiculosPagados);

                retValue = false;
            }
            else if (!IsNumeracionOk() && ModoPermite(ePermisosModos.AutorizarNumeracion))
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": " + eMensajesPantalla.VerificarNumeracion.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaNoInicializada.GetDescription() + " - " + eMensajesPantalla.VerificarNumeracion.GetDescription());
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);
                }

                retValue = false;
            }
            else if (operador != null && int.Parse(operador?.NivelAcceso) != (int)eNivelUsuario.Supervisor)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": " + eMensajesPantalla.UsuarioNoSupervisor.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.UsuarioNoSupervisor.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.UsuarioNoSupervisor);

                retValue = false;
            }
            else if (_estado == eEstadoVia.EVQuiebreBarrera)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": " + eMensajesPantalla.QuiebreYaIniciado.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.QuiebreYaIniciado.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.QuiebreYaIniciado);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Inicia el modo quiebre liberado
        /// </summary>
        /// <param name="origenComando"></param>
        private async Task InicioQuiebre(eOrigenComando origenComando, bool EnviarSetApertura = true)
        {
            _modoQuiebre = eQuiebre.EVQuiebreLiberado;
            _estado = eEstadoVia.EVQuiebreBarrera;
            _turno.EstadoTurno = enmEstadoTurno.Quiebre;
            _turno.ModoQuiebre = 'S';
            _turno.Mantenimiento = 'N';
            ulong auxLong = await ModuloBaseDatos.Instance.ObtenerNumeroTurnoAsync();
            _turno.NumeroTurno = auxLong == 0 ? _turno.NumeroTurno : auxLong;

            //	Evento de inicio de quiebre
            if (origenComando == eOrigenComando.Supervision)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": OK");
                _turno.PcSupervision = 'S';
                _turno.CodigoTurno = "PC";
            }

            Modos modo = new Modos();

            modo.Modo = "D";
            modo.Descripcion = "Dinamico";

            SetModo(modo);
            AsignarModoATurno();

            //Consulto el parte
            await ConsultarParte();

            _turno.Parte.NombreCajero = _operadorActual.Nombre;
            _turno.Parte.IDCajero = _operadorActual.ID;

            ulong numeroTarjeta;
            ulong.TryParse(_operadorActual.ID, out numeroTarjeta);

            _turno.NumeroTarjeta = numeroTarjeta;
            _turno.Operador = _operadorActual;
            _turno.PcSupervision = 'N';
            _turno.CodigoTurno = "";
            _turno.Modo = _modo.Modo;
            _turno.FechaApertura = EnviarSetApertura ? Fecha : _turno.FechaApertura;

            ModuloEventos.Instance.SetCambioModo(_turno, 0, 'L');

            IniciarTimerSeteoDAC();

            // Se almacena turno en la BD local y Se envia el evento de apertura de bloque
            if (EnviarSetApertura)
            {
                await ModuloBaseDatos.Instance.AlmacenarTurnoAsync(_turno);
                ModuloEventos.Instance.SetAperturaBloque(ModuloBaseDatos.Instance.ConfigVia, _turno);
            }

            List<DatoVia> listaDatosVia = new List<DatoVia>();

            // Se actualiza el estado de los perifericos
            if (ModoPermite(ePermisosModos.SemaforoPasoVerdeEnQuiebre))
            {
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
            }

            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via); //La barrera no baja hasta que la cierren con la tecla Baja Barrera o terminen el quiebre
            _logger?.Debug("InicioQuiebre -> BARRERA ARRIBA!!");
            DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Verde);
            DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);

            // Actualiza estado de los mimicos en pantalla
            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.AbrirVia);

            // Actualiza datos de turno en pantalla
            ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

            //Actualizar vehiculo en pantalla
            Vehiculo vehiculo = new Vehiculo();
            vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
            vehiculo.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

            // Actualiza mensaje en pantalla
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Abierta"));
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir(ClassUtiles.GetEnumDescr(eMensajesPantalla.InicioQuiebre)) + " " + Traduccion.Traducir("Liberado"));

            _logger?.Debug(ClassUtiles.GetEnumDescr(eMensajesPantalla.InicioQuiebre) + " Liberado");

            // Actualiza display
            ModuloDisplay.Instance.Enviar(eDisplay.QUI);
            //ModuloDisplay.Instance.Enviar( eDisplay.VAR, null, "QUIEBRE" );

            ModuloAntena.Instance.ActivarAntena();

            // Eventos de inicio de quiebre y apertura

            ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

            //Borrar lista de tags del servicio de antena
            ModuloAntena.Instance.BorrarListaTags();

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.ViaModoLiberado, 0);

            List<DatoVia> listaDatosVia3 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);

        }

        #endregion

        #region Fin Quiebre Liberado

        /// <summary>
        /// Procesa la tecla FINALIZAR QUIEBRE enviada desde pantalla,
        /// recibiendo string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaFinQuiebre(ComandoLogica comando)
        {
            await ProcesarFinQuiebre(eOrigenComando.Pantalla);
        }

        /// <summary>
        /// Finaliza el quiebre liberado, realizando el cierre de turno 
        /// y enviando los eventos correspondientes
        /// </summary>
        /// <param name="origenComando"></param>
        private async Task ProcesarFinQuiebre(eOrigenComando origenComando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesFinQuiebre(origenComando))
            {
                if (origenComando == eOrigenComando.Pantalla)
                {
                    // Solicito confirmacion para cerrar turno en quiebre
                    Causa causa = new Causa(eCausas.FinalizarQuiebre, ClassUtiles.GetEnumDescr(eCausas.FinalizarQuiebre));
                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
                }
                else
                {
                    // Si el comando llega desde supervisión (ni automatico) no se debe confirmar
                    await FinQuiebre(origenComando);

                    if (origenComando == eOrigenComando.Supervision)
                        ResponderComandoSupervision("E");
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder realizar un fin de quiebre
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesFinQuiebre(eOrigenComando origenComando)
        {
            bool retValue = true;

            if (_estado != eEstadoVia.EVQuiebreBarrera)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Fin Quiebre") + ": " + eMensajesPantalla.NoQuiebreLiberado.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.NoQuiebreLiberado.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoQuiebreLiberado);

                retValue = false;
            }
            else if (DAC_PlacaIO.Instance.IsBarreraAbierta(eTipoBarrera.Via))
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Fin Quiebre") + ": " + eMensajesPantalla.BarreraAbierta.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.BarreraAbierta.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BarreraAbierta);

                retValue = false;
            }
            else if (_estado != eEstadoVia.EVQuiebreBarrera)
            {
                if (origenComando == eOrigenComando.Supervision)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Fin Quiebre") + ": " + eMensajesPantalla.QuiebreNoIniciado.GetDescription());
                    ResponderComandoSupervision("X", eMensajesPantalla.QuiebreNoIniciado.GetDescription());
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.QuiebreNoIniciado);

                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Finaliza el quiebre liberado
        /// </summary>
        private async Task FinQuiebre(eOrigenComando origenComando)
        {
            await CierreTurno(false, eCodigoCierre.FinQuiebreLiberado, origenComando);
            _modoQuiebre = eQuiebre.Nada;

            ulong violacionesQuiebreBarrera = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.ViolacionesQuiebreBarrera);

            // Se envia el evento de cambio de modo por final de quiebre
            if (origenComando == eOrigenComando.Supervision)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Fin Quiebre") + ": OK");
                _turno.PcSupervision = 'S';
            }

            ModuloEventos.Instance.SetCambioModo(_turno, violacionesQuiebreBarrera, 'N');
            _turno.PcSupervision = 'N';

            ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

            ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.ViaModoLiberado, 0);

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            _ultimaAccion.Clear();

            _logger?.Debug("Fin Quiebre Liberado");
        }

        #endregion

        #region Tag Manual

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA TAG MANUAL
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaTagManual(ComandoLogica comando)
        {
            if (_turno.EstadoTurno == enmEstadoTurno.Quiebre)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EnQuiebre);
            else
            {
                string patente = string.Empty;

                
                patente = _logicaVia.GetPrimerVehiculo().Patente; 

                await ProcesarTagManual(patente, null, eOrigenComando.Pantalla);
            }
        }

        /// <summary>
        /// Recibe de pantalla un vehiculo con los datos correspondientes 
        /// al tag manual ingresado
        /// </summary>
        /// <param name="comando"></param>
        public override async void SalvarTagManual(ComandoLogica comando)
        {
            try
            {
                //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                if (!_ultimaAccion.MismaAccion(enmAccion.TAG) || _ultimaAccion.AccionVencida())
                    _ultimaAccion.Clear();

                if (!_ultimaAccion.AccionEnProceso())
                {
                    _logger.Debug("SalvarTagManual -> Accion Actual: {0}", enmAccion.TAG);
                    _ultimaAccion.GuardarAccionActual(enmAccion.TAG);

                    Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);

                    Tag tag = new Tag();
                    tag.NumeroTag = vehiculo.InfoTag.NumeroTag;
                    tag.NumeroTID = vehiculo.InfoTag.Tid;

                    await ProcesarTagManual(tag.NumeroTag, tag, eOrigenComando.Pantalla);
                }
                else
                    _logger.Debug("SalvarTagManual -> PROCESANDO Accion: {0}", enmAccion.TAG);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta el Pagado
        /// </summary>
        /// <param name="patente"></param>
        /// <param name="vehiculo"></param>
        /// <param name="origenComando"></param>
        private async Task ProcesarTagManual(string patente, Tag tag, eOrigenComando origenComando, bool formatoPatValido = true)
        {
            if (await ValidarPrecondicionesTagManual(origenComando, eTipoLecturaTag.Manual))
            {
                Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

                if (tag != null)
                {
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    _logicaVia.ProcesarLecturaTag(eEstadoAntena.Ok, tag, eTipoLecturaTag.Manual);
                }
                else if (!string.IsNullOrEmpty(patente))
                {
                    InfoTag infoTag = new InfoTag();
                    TagBD tagBD = new TagBD();

                    vehiculo.Patente = patente;

                    infoTag.OrigenSaldo = 'O';
                    tagBD = ModuloBaseDatos.Instance.ObtenerTagEnLinea(patente, "O", "P");

                    // Si falla la consulta de patente online
                    if (tagBD?.EstadoConsulta != EnmStatusBD.OK && tagBD?.EstadoConsulta != EnmStatusBD.SINRESULTADO)
                    {
                        tagBD = new TagBD();
                        tagBD = await ModuloBaseDatos.Instance.BuscarTagPorPatenteAsync(patente);
                        infoTag.OrigenSaldo = 'L';
                    }

                    // Si encontro un tag
                    if (tagBD?.EstadoConsulta == EnmStatusBD.OK)
                    {
                        if(tagBD.Estado == 103)
                        {
                            ModuloPantalla.Instance.LimpiarMensajes();
                            _logicaVia.GetPrimeroSegundoVehiculo().Patente = tagBD.NumeroTag;
                            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.SalidaVehiculo);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1,"La cuenta esta vencida");
                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(_logicaVia.GetPrimeroSegundoVehiculo(), ref listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);
                            return;
                        }

                        if (tagBD.Habilitado == 'N')
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, tagBD.MensajePantalla);
                        }

                        if (tagBD.TipoTarifa != null)
                        {
                            // Se busca la tarifa
                            TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(tagBD.Categoria, (byte)tagBD.TipoTarifa);
                            Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                            infoTag.Patente = tagBD.Patente;
                            infoTag.NumeroTag = tagBD.NumeroTag;
                            infoTag.Marca = tagBD.Marca;
                            infoTag.Modelo = tagBD.Modelo;
                            infoTag.Color = tagBD.Color;
                            infoTag.NombreCuenta = tagBD.NombreCliente;
                            infoTag.Categoria = (short)tagBD.Categoria;
                            infoTag.CategoDescripcionLarga = tarifa?.Descripcion;
                            infoTag.PagoEnVia = (char)tagBD.PagoEnVia;
                            infoTag.TipoSaldo = tagBD.TipoSaldo;
                            infoTag.Tarifa = tarifa.Valor;
                            infoTag.TipoTarifa = (byte)tagBD.TipoTarifa;


                            vehiculo.InfoTag = infoTag;
                            vehiculo.Patente = infoTag.Patente;
                            vehiculo.Tarifa = tarifa.Valor;

                            List<DatoVia> listaDatosVia = new List<DatoVia>();

                            ClassUtiles.InsertarDatoVia(infoTag, ref listaDatosVia);
                            ClassUtiles.InsertarDatoVia(tagBD, ref listaDatosVia);

                            ModuloPantalla.Instance.LimpiarMensajes();
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.TAGMANUAL, listaDatosVia);
                        }
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Ingrese la patente nuevamente"));

                        if (tagBD?.EstadoConsulta == EnmStatusBD.ERRORBUSQUEDA || tagBD?.EstadoConsulta == EnmStatusBD.EXCEPCION)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.PatenteErrorBusqueda);
                        }
                        else if (tagBD?.EstadoConsulta == EnmStatusBD.SINRESULTADO)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.PatenteSinTag);

                            if (!formatoPatValido)
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, eMensajesPantalla.ErrorFormatoPatente);
                        }
                        else if (tagBD?.EstadoConsulta == EnmStatusBD.TIMEOUT)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.PatenteTimeout);
                        }
                        await ProcesarPatente(eTipoComando.eTecla, vehiculo, eCausas.TagManual, false, false);
                    }
                }
                else
                {
                    ModuloPantalla.Instance.LimpiarMensajes();


                    await ProcesarPatente(eTipoComando.eTecla, vehiculo, eCausas.TagManual, true);
                }
            }

            _ultimaAccion.Clear();
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder realizar un tag manual
        /// </summary>
        /// <param name="vehiculo"></param>
        /// <param name="origenComando"></param>
        /// <returns></returns>
        private async Task<bool> ValidarPrecondicionesTagManual(eOrigenComando origenComando, eTipoLecturaTag lectura)
        {
            bool retValue = true;

            ePermisosModos permisoOp = lectura == eTipoLecturaTag.Manual ? ePermisosModos.TagManual : ePermisosModos.CobroConChip;
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(permisoOp))
                {
                    if (!ModoPermite(ePermisosModos.TagManual))
                    {
                        if (lectura == eTipoLecturaTag.Chip)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteChip);
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);

                        retValue = false;
                    }
                }
                else if (vehiculo.EstaPagado && !_logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0)
                {
                    if (origenComando == eOrigenComando.Pantalla || lectura == eTipoLecturaTag.Chip)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);
                        retValue = false;
                    }
                    else if (origenComando == eOrigenComando.Supervision)
                    {
                        //Asignar categoría autotabulante al vehiculo
                        if (ModoPermite(ePermisosModos.Autotabular))
                            await Categorizar((short)ModuloBaseDatos.Instance.ConfigVia.MonoCategoAutotab.GetValueOrDefault());
                    }
                }

                char chTipop = lectura == eTipoLecturaTag.Manual ? 'T' : 'C';
                bool categoFPagoValida = await CategoriaFormaPagoValida(chTipop, '\0', vehiculo.Categoria);

                if (retValue && !categoFPagoValida && lectura != eTipoLecturaTag.Chip)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.TagManualSobreLazo) && _logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (vehiculo.EsperaRecargaVia && lectura != eTipoLecturaTag.Chip && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                {
                    if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                    {
                        if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                        else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                        retValue = false;
                    }
                }

                //no vuelvo a revisar si hay cambio de tarifa, ya lo hizo al categorizar
                if (lectura != eTipoLecturaTag.Chip)
                {
                    if (EsCambioTarifa(false))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                        retValue = false;
                    }
                }
                else if (vehiculo.EstaPagado && _logicaVia.EstaOcupadoLazoSalida())
                {
                    retValue = false;
                }

                if (retValue)
                {
                    if (EsCambioJornada())
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                        retValue = false;
                    }
                    else if (_modoQuiebre != eQuiebre.Nada && !ModoPermite(ePermisosModos.CobroEnQuiebre))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                        retValue = false;
                    }

                    //vuelvo a traer el veh antes de validar esta precondición
                    vehiculo = _logicaVia.GetPrimerVehiculo();

                    if (vehiculo.ProcesandoViolacion)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                        retValue = false;
                    }
                }

                //Borrar datos del chip y limpiar mensajes
                if (retValue && vehiculo.InfoTag.TipOp == 'C' && !vehiculo.EstaPagado && vehiculo.ListaRecarga.Count() == 0)
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ClearChip(vehiculo);
                }
            }

            return retValue;
        }

        #endregion

        #region Vale Prepago
        private async Task<bool> ValidarPrecondicionesValePrepago()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado != eEstadoVia.EVAbiertaLibre && _estado != eEstadoVia.EVAbiertaCat)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.ValePrepago))
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                retValue = false;
            }
            else if (vehiculo.EstaPagado)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                retValue = false;
            }
            else if (vehiculo.Categoria <= 0 || _modo.Cajero != "S")
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado + "o");
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.ModoConCajero);
                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.CobroManualSobreLazo) && _logicaVia.EstaOcupadoLazoSalida())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                retValue = false;
            }
            else if (vehiculo.EsperaRecargaVia)
            {
                if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas))
                {
                    if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaPagoEnVia);
                    else if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                        return retValue;
                    else if (vehiculo.TipoVenta != eVentas.Nada)
                    {
                        retValue = false;
                        return retValue;
                    }
                    else
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);

                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                    retValue = false;
                }
            }
            else if (EsCambioTarifa(false))
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                retValue = false;
            }
            //else if( La fecha actual es la misma que la fecha en que se abrió el turno )
            //{
            //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa );
            //    retValue = false;
            //}

            bool categoFPagoValida = await CategoriaFormaPagoValida('E', '\0', vehiculo.Categoria);

            if (retValue && !categoFPagoValida)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                retValue = false;
            }
            else if (ModuloBaseDatos.Instance.ConfigVia.LeeClearing != "S")
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.APNoConfigurada);
                retValue = false;
            }

            //vuelvo a traer el veh antes de validar esta precondición
            vehiculo = _logicaVia.GetPrimerVehiculo();

            if (vehiculo.ProcesandoViolacion)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                retValue = false;
            }

            return retValue;
        }



        private async Task PagadoValePrepago(InfoClearing clearing)
        {
            try
            {
                Vehiculo vehiculo;
                vehiculo = _logicaVia.GetPrimerVehiculo();
                ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
                //vehiculo.CobroEnCurso = true;
                //TODO: Ver el formato del ticket impreso, y recalcular la tarifa con el tipo de tarifa asociada

                // Se busca la tarifa
                TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);

                Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(vehiculo.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                vehiculo.CategoriaDesc = tarifa?.Descripcion;
                vehiculo.Tarifa = tarifa?.Valor ?? 0;
                vehiculo.NoPermiteTag = true;
                //vehiculo.CobroEnCurso = true;
                vehiculo.TipOp = 'E';
                vehiculo.TipBo = ' ';
                vehiculo.FormaPago = eFormaPago.CTVale;
                vehiculo.Fecha = Fecha;
                vehiculo.FechaFiscal = vehiculo.Fecha;
                vehiculo.InfoClearing = clearing;
                if (vehiculo.InfoTag != null)
                {
                    vehiculo.Patente = vehiculo.InfoTag.Patente == vehiculo.Patente ? "" : vehiculo.Patente;
                    vehiculo.InfoTag.Clear();
                }

                if (_esModoMantenimiento)
                {
                    vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                        vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                }
                else
                {
                    vehiculo.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);
                    if (vehiculo.AfectaDetraccion == 'S')
                        vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);
                }

                vehiculo.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                InfoCliente cliente = new InfoCliente();
                cliente.RazonSocial = "CONSUMIDOR FINAL";
                cliente.Ruc = "9999999999999";
                vehiculo.InfoCliente = cliente;

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                {
                    ImprimiendoTicket = true;

                    errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Efectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                    ImprimiendoTicket = false;
                }

                ModuloPantalla.Instance.LimpiarMensajes();

                //Si no se imprimió ticket, no incremento el numero de ticket (Ojo: no sacar esto de acá!)
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel)
                {
                    if (_esModoMantenimiento)
                    {
                        vehiculo.NumeroTicketNF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    }
                    else
                    {
                        vehiculo.NumeroTicketF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                    }
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    {
                        vehiculo.NumeroDetraccion = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);
                    }
                }

                //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    vehiculo.NoPermiteTag = false;
                    vehiculo.CobroEnCurso = false;
                    vehiculo.TipOp = ' ';
                    vehiculo.FormaPago = eFormaPago.Nada;
                    vehiculo.Tarifa = 0;

                    // Envia el error de impresora a pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                }
                else
                {
                    _estado = eEstadoVia.EVAbiertaPag;

                    if (ModuloImpresora.Instance.UltimoTicketLegible == "")
                    {
                        //Consultar nuevamente por el ticket 
                        errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.Efectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);

                        //Vuelvo a buscar el vehiculo
                        ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    }

                    vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                    //Se incrementa tránsito
                    vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                    // Se actualizan perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    // Actualiza el mensaje en pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoValePrepago);

                    _logger?.Debug(ClassUtiles.GetEnumDescr(eMensajesPantalla.PagadoValePrepago));

                    // Actualiza el estado de vehiculo en pantalla
                    listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    //Capturo foto y video
                    _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);
                    //almacena la foto
                    ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                    // Envia mensaje a display
                    ModuloDisplay.Instance.Enviar(eDisplay.PAG);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);
                    //Se envía setCobro
                    vehiculo.Operacion = "CB";
                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    //Se envía setCobro
                    vehiculo.Operacion = "CB";
                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                    _logicaVia.GetVehOnline().CategoriaProximo = 0;
                    _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                    // Adelantar Vehiculo
                    _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                    // Update Online
                    ModuloEventos.Instance.ActualizarTurno(_turno);
                    UpdateOnline();
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        #endregion

        #region Observacion

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA OBSERVACION
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaObservacion(ComandoLogica comando)
        {
            if (_estado == eEstadoVia.EVAbiertaCat)
                await ProcesarObservacion(eTipoComando.eTecla, null);
            else
            {
                if (_logicaVia.GetVehObservado().NoVacio)
                {
                    if (_logicaVia.GetVehObservado().Operacion == "VI")
                        await ProcesarObservacionViolacion(eTipoComando.eTecla, null);
                    else
                        await ProcesarObservacion(eTipoComando.eTecla, null);
                }
            }
        }

        public async void TeclaObservacion(eOpcionMenu comando)
        {
            if (comando == eOpcionMenu.Observaciones || comando == eOpcionMenu.ObservacionesTransitoAnterior)
            {
                await ProcesarObservacion(eTipoComando.eTecla, null);
            }
            else if (comando == eOpcionMenu.ObservacionesViolacion)
            {
                await ProcesarObservacionViolacion(eTipoComando.eTecla, null);
            }
        }



        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta la asignacion de la observacion
        /// </summary>
        /// <param name="tipoComando"></param>
        private async Task ProcesarObservacion(eTipoComando tipoComando, Observacion observacion)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            bool mostrarPantallaPrincipal = true;

            if (ValidarPrecondicionesObservacion())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    // Se consulta la lista de observaciones al modulo de BD 
                    List<Observacion> listaCausasObservacion = await ModuloBaseDatos.Instance.BuscarListaObservacionesAsync();

                    if (listaCausasObservacion?.Count > 0)
                    {
                        ListadoOpciones opciones = new ListadoOpciones();

                        Causa causa = new Causa();
                        causa.Codigo = eCausas.Observacion;
                        causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.Observacion);

                        foreach (Observacion obser in listaCausasObservacion)
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(obser), false, obser.Texto, string.Empty, obser.Orden, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        // Envio la lista de observaciones a pantalla
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                        mostrarPantallaPrincipal = false;
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron observaciones"));
                    }
                }
                else if (tipoComando == eTipoComando.eSeleccion)
                {
                    AsignarObservacion(observacion);
                }
            }

            if (mostrarPantallaPrincipal)
            {
                if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
        }

        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta la asignacion de la observacion de violacion
        /// </summary>
        /// <param name="tipoComando"></param>
        private async Task ProcesarObservacionViolacion(eTipoComando tipoComando, ObservacionViol observacionViol)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesObservacion())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    // Se consulta la lista de observaciones de violacion al modulo de BD 
                    List<ObservacionViol> listaCausasObservacionViol = await ModuloBaseDatos.Instance.BuscarObservacionViolacionAsync();

                    if (listaCausasObservacionViol?.Count > 0)
                    {
                        ListadoOpciones opciones = new ListadoOpciones();

                        Causa causa = new Causa();
                        causa.Codigo = eCausas.ObservacionViol;
                        causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.ObservacionViol);

                        foreach (ObservacionViol obser in listaCausasObservacionViol)
                        {
                            CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(obser), false, obser.Texto, string.Empty, obser.Orden, false);
                        }

                        opciones.MuestraOpcionIndividual = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                        // Envio la lista de observaciones de violacion a pantalla
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron observaciones de violacion"));
                    }
                }
                else if (tipoComando == eTipoComando.eSeleccion)
                {
                    Observacion observacion = new Observacion();

                    observacion.Codigo = observacionViol.Codigo;
                    observacion.Orden = observacionViol.Orden;
                    observacion.Texto = observacionViol.Texto;

                    AsignarObservacion(observacion);
                }
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder asignar una observacion
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesObservacion()
        {
            bool retValue = true;

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            //else if (_modo.Cajero == "N")
            //{
            //    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);
            //    retValue = false;
            //}

            ulong vehiculosDesdeApertura = 0;

            vehiculosDesdeApertura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito) -
                                        ModuloBaseDatos.Instance.BuscarValorInicialContador(eContadores.NumeroTransito);

            if (retValue && vehiculosDesdeApertura < 0)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoHayObservacion);
                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Se asigna la observacion al vehiculo y se genera el evento de observacion, 
        /// tanto para transitos pagados como para violaciones
        /// </summary>
        /// <param name="observacion"></param>
        private void AsignarObservacion(Observacion observacion)
        {
            bool hayObservacion = true;
            bool enviaEvento = true;

            ObsTransito obsTransito = new ObsTransito();
            obsTransito.TipoObservacion = "T";
            obsTransito.CodJustificacion = (byte)observacion.Codigo;
            obsTransito.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);

            if (_logicaVia.GetPrimerVehiculo().EstaPagado)
            {
                _logicaVia.GetPrimerVehiculo().TipoObservacion = eTipoObservacion.Observacion;
                _logicaVia.GetPrimerVehiculo().CodigoObservacion = (byte)observacion.Codigo;
                enviaEvento = false;
            }
            else if (_logicaVia.GetVehiculoAnterior() != null) // Existe un vehiculo anterior
            {
                _logicaVia.GetVehiculoAnterior().TipoObservacion = eTipoObservacion.Observacion;

                if (_logicaVia.GetVehObservado().Operacion == "VI")
                {
                    obsTransito.TipoObservacion = "V";
                    _logicaVia.GetVehiculoAnterior().TipoObservacion = eTipoObservacion.ObservacionViolacion;
                }
            }
            else
            {
                hayObservacion = false;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoHayObservacion);
            }

            if (hayObservacion)
            {
                // Si la observacion es para el transito actual, no se envia el evento
                if (enviaEvento)
                    ModuloEventos.Instance.SetObsTransito(obsTransito);

                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Se Asignó la Observación") + ":");
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, observacion.Texto);

                _logger?.Debug("Se Asignó la Observación: " + observacion.Texto);

                //Limpio el vehiculo observado para no poder observarlo nuevamente
                _logicaVia.LimpiarVehObservado();
            }
        }

        #endregion

        #region Video Interno

        public override async void TeclaVideoInterno(ComandoLogica comando)
        {
            await ProcesarVideoInterno(eTipoComando.eTecla, null);
        }

        public async void TeclaVideoInterno(eOpcionMenu comando)
        {
            await ProcesarVideoInterno(eTipoComando.eTecla, null);
        }

        private async Task ProcesarVideoInterno(eTipoComando tipoComando, Observacion observacion)
        {
            ModuloPantalla.Instance.LimpiarMensajes();
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            eCausaVideo sensor = eCausaVideo.Manual;

            if (!vehiculo.ListaInfoVideo.Exists(item => item.Camara == eCamara.Interna))
            {
                _logicaVia.CapturaVideo(ref vehiculo, ref sensor, true);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Video interno iniciado");
            }              
            else
                vehiculo.ListaInfoVideo.Find(item => item.EstaFilmando == true && item.Camara == eCamara.Interna).Causa = eCausaVideo.Manual;
            
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.VIDEOINTERNO, listaDatosVia);
            if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
            else
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);
        }

        #endregion

        #region Mensajes a Supervision

        /// <summary>
        /// Recibe un string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaMensajeASupervision(ComandoLogica comando)
        {
            await ProcesarMensajeASupervision(eTipoComando.eTecla, null);
        }

        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta que se envia el mensaje
        /// </summary>
        /// <param name="tipoComando"></param>
        /// <param name="mensajeSupervision"></param>
        private async Task ProcesarMensajeASupervision(eTipoComando tipoComando, MensajeSupervision mensajeSupervision)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (tipoComando == eTipoComando.eTecla)
            {
                List<MensajeSupervision> listaMensajesSupervision = await ModuloBaseDatos.Instance.BuscarMensajeSupervisionAsync();

                if (listaMensajesSupervision?.Count > 0)
                {
                    ListadoOpciones opciones = new ListadoOpciones();

                    Causa causa = new Causa();
                    causa.Codigo = eCausas.MensajeSupervision;
                    causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.MensajeSupervision);

                    int orden = 1;

                    foreach (MensajeSupervision causas in listaMensajesSupervision)
                    {
                        CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causas), true, causas.Texto, string.Empty, orden++, false);
                    }

                    opciones.MuestraOpcionIndividual = false;

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                    // Envio la lista de mensajes a supervision a pantalla
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                }
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron mensajes a supervisión"));
                }
            }
            else if (tipoComando == eTipoComando.eSeleccion)
            {
                await EnviarMensajeSupervision(mensajeSupervision);
            }
        }

        /// <summary>
        /// Envia el evento correspondiente al mensaje
        /// </summary>
        /// <param name="mensajeSupervision"></param>
        private async Task EnviarMensajeSupervision(MensajeSupervision mensajeSupervision)
        {
            EnmStatusBD respuestaEvento = await ModuloEventos.Instance.SetMensajeAsync(_turno, (byte)mensajeSupervision.Codigo);

            if (respuestaEvento != EnmStatusBD.OK)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error enviando mensaje a supervisión"));
            else
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.MsgSupEnviado, Fecha.ToString("HH:mm:ss") + " - " + mensajeSupervision.Texto);
        }

        #endregion

        #region Opciones Tecnico

        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta la ejecucion de la opcion seleccionada
        /// </summary>
        /// <param name="aperturaTecnico"></param>
        private async Task ProcesarMenuTecnico(eOpcionesTecnico aperturaTecnico)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (ValidarPrecondicionesTecnico())
            {
                _logger.Info("ProcesarMenuTecnico -> [{0}]", aperturaTecnico.ToString());
                switch (aperturaTecnico)
                {
                    case eOpcionesTecnico.Apagado:
                        ApagarTerminal();
                        break;

                    case eOpcionesTecnico.CierreSesion:
                        CerrarSesion();
                        break;

                    case eOpcionesTecnico.ConfirmarNumeracion:
                        ProcesarNumeracion(_operadorActual);
                        break;

                    case eOpcionesTecnico.ConsultarNumeracion:
                    case eOpcionesTecnico.VerificarNumeracion:
                        ConsultarNumeracion(_operadorActual);
                        break;

                    case eOpcionesTecnico.ModoMantenimiento:
                        if (!DAC_PlacaIO.Instance.EntradaBidi())
                            await EnviaModos(null, _operadorActual, eOrigenComando.Pantalla, eNivelUsuario.Tecnico);
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaOpuestaAbierta);
                        break;

                    case eOpcionesTecnico.Reinicio:
                        ReiniciarTerminal();
                        break;

                    case eOpcionesTecnico.Testeo:
                        AbrirTesteo();
                        break;

                    case eOpcionesTecnico.Versiones:
                        VersionesInstaladas();
                        break;
                }
            }
        }

        private void VersionesInstaladas()
        {
            List<EventoVersion> listaVersiones = new List<EventoVersion>();
            EventoVersion version = null;
            //Obtengo las versiones de cada servicio
            //Logica
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "LOGICA"), Traduccion.Traducir("Servicio de Lógica de Vía"));
            if (version != null)
                listaVersiones.Add(version);

            //Antena
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "ANTENA"), Traduccion.Traducir("Servico de Antena"));
            if (version != null)
                listaVersiones.Add(version);

            //BaseDatos
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "BASEDATOS"), Traduccion.Traducir("Servicio de Base de Datos Local"));
            if (version != null)
                listaVersiones.Add(version);

            //Display
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "DISPLAY"), Traduccion.Traducir("Servicio de Display"));
            if (version != null)
                listaVersiones.Add(version);

            //Foto
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "FOTO"), Traduccion.Traducir("Servicio de Foto"));
            if (version != null)
                listaVersiones.Add(version);

            //Impresora
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "IMPRESORA"), Traduccion.Traducir("Servicio de Impresora"));
            if (version != null)
                listaVersiones.Add(version);

            //Pantalla
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "PANTALLA"), Traduccion.Traducir("Pantalla"));
            if (version != null)
                listaVersiones.Add(version);

            //Tarjeta Chip
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "TARJETACHIP"), Traduccion.Traducir("Servicio de Tarjeta Chip"));
            if (version != null)
                listaVersiones.Add(version);

            //Video
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "VIDEO"), Traduccion.Traducir("Servicio de Video"));
            if (version != null)
                listaVersiones.Add(version);

            //VideoContinuo
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "VIDEOCONTINUO"), Traduccion.Traducir("Servicio Video Continuo"));
            if (version != null)
                listaVersiones.Add(version);

            //Monitor
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "MONITOR"), Traduccion.Traducir("Monitor de Servicios"));
            if (version != null)
                listaVersiones.Add(version);

            //Testeo
            version = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("PATH_SERVICIOS", "TESTEO"), Traduccion.Traducir("Testeo"));
            if (version != null)
                listaVersiones.Add(version);

            Causa causa = new Causa(eCausas.Versiones, ClassUtiles.GetEnumDescr(eCausas.Versiones));

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(listaVersiones, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.VERSIONES, listaDatosVia);
        }

        /// <summary>
        /// Confirma la numeracion y queda la via inicializada
        /// </summary>
        private void ConfirmarNumeracion(ComandoLogica comando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();
            _logger.Info("Confirmar Numeracion -> Inicio");
            if (comando.CodigoStatus == enmStatus.Ok)
            {
                try
                {
                    Operador operador = ClassUtiles.ExtraerObjetoJson<Operador>(comando.Operacion);
                    _seConfirmoNum = true;
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Confirmada la numeración actual"));
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Puede abrir el turno"));
                    _estadoNumeracion = eEstadoNumeracion.NumeracionOk;
                    InicializarPantalla(false); //Inicializo la pantalla correctamente porque ya tengo confirmada la numeracion
                    SetEstadoNumeracion(_estadoNumeracion, _numeracion, false);
                    ModuloEventos.Instance.SetAutorizaNumeracion(_turno, operador);
                    ModuloBaseDatos.Instance.ActualizarEstadoNumeracion(_estadoNumeracion);
                    _turno.EstadoNumeracion = _estadoNumeracion;

                    // Update Online
                    ModuloEventos.Instance.ActualizarTurno(_turno);
                    UpdateOnline();
                    _logger.Info("Confirmar Numeracion -> Fin");
                }
                catch (Exception e)
                {
                    _loggerExcepciones?.Error(e);
                }
            }
            else if (comando.CodigoStatus == enmStatus.Abortada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No confirmada la numeración actual"));
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, string.Empty);
            }
        }

        private async void OnTimerCerrarModoMantenimiento(object source, ElapsedEventArgs e)
        {
            if (_turno.Mantenimiento == 'S')
            {
                //Cierro el turno o bloqueo la operatoria
                await CierreTurno(false, eCodigoCierre.FinalTurno, eOrigenComando.Pantalla);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("El tiempo de Modo Mantenimiento ha finalizado"));
            }
        }

        /// <summary>
        /// Apertura de turno en Modo Mantenimiento
        /// </summary>
        private async Task AperturaModoMantenimiento(Modos modo, bool EnviarSetApertura = true, bool confirmar = false)
        {
            List<DatoVia> listaDatosVia = new List<DatoVia>();

            if (confirmar)
            {
                Causa causa = new Causa(eCausas.AperturaTurnoMantenimiento, eCausas.AperturaTurnoMantenimiento.GetDescription());

                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(modo, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);

                return;
            }

            if (!IsNumeracionOk())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);
                return;
            }

            _esModoMantenimiento = true;
            _estado = eEstadoVia.EVAbiertaLibre;

            SetModo(modo);

            DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Rojo);
            DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
            _logger?.Debug("AperturaModoMantenimiento -> BARRERA ABAJO!!");
            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);

            // Incremento alarma de apertura en modo mantenimiento
            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.ViaAbiertaMantenimiento, 0);

            Mimicos mimicos = new Mimicos();
            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            listaDatosVia.Clear();
            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);

            // Actualiza las variables necesarios de Turno y los refresca en pantalla
            AsignarModoATurno();

            _turno.Modo = modo.Modo;
            _turno.EstadoTurno = enmEstadoTurno.Abierta;
            _turno.Mantenimiento = 'S';
            _turno.FechaApertura = EnviarSetApertura ? Fecha : _turno.FechaApertura;
            _turno.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido == 'A' ? enmSentido.Ascendente : enmSentido.Descendente;

            _turno.OrigenApertura = 'M';
            _turno.PcSupervision = 'N';
            _turno.ModoQuiebre = 'N';

            _turno.Parte.NombreCajero = _operadorActual.Nombre;
            _turno.Parte.IDCajero = _operadorActual.ID;

            ulong numeroTarjeta;
            ulong.TryParse(_operadorActual.ID, out numeroTarjeta);
            _turno.NumeroTarjeta = numeroTarjeta;
            _turno.Operador = _operadorActual;

            ulong auxLong = await ModuloBaseDatos.Instance.ObtenerNumeroTurnoAsync();
            _turno.NumeroTurno = auxLong == 0 ? _turno.NumeroTurno : auxLong;

            listaDatosVia.Clear();
            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

            //Actualizar vehiculo en pantalla
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
            vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketNoFiscal);
            vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
            vehiculo.BorrarNumTicket = true;

            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
            vehiculo.NumeroTicketNF = 0; //se limpia numero de ticket
            vehiculo.NumeroDetraccion = 0;

            // Enviar mensaje a Display
            ModuloDisplay.Instance.Enviar(eDisplay.BNV);

            ModuloAntena.Instance.ActivarAntena();

            //Para limpiar el flag de sentido opuesto, sino la vía no genera violaciones.
            _logicaVia.UltimoSentidoEsOpuesto = false;

            // Se almacena turno en la BD local y Se envia el evento de apertura de bloque
            if (EnviarSetApertura)
            {
                await ModuloBaseDatos.Instance.AlmacenarTurnoAsync(_turno);
                ModuloEventos.Instance.SetAperturaBloque(ModuloBaseDatos.Instance.ConfigVia, _turno);
            }

            IniciarTimerSeteoDAC();

            //Inicio el timer de cierre de turno de mantenimiento
            _timerCierreModoMantenimiento.Elapsed -= new ElapsedEventHandler(OnTimerCerrarModoMantenimiento);
            _timerCierreModoMantenimiento.Elapsed += new ElapsedEventHandler(OnTimerCerrarModoMantenimiento);
            _timerCierreModoMantenimiento.Interval = 1000 * 60 * _minutosCierreModoMantenimiento;
            _timerCierreModoMantenimiento.AutoReset = false;
            _timerCierreModoMantenimiento.Enabled = true;

            // Resetear contadores
            //Se encarga el modulo de base de datos al cambiar el turno

            if (ModoPermite(ePermisosModos.Autotabular))
                await Categorizar((short)ModuloBaseDatos.Instance.ConfigVia.MonoCategoAutotab);

            ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

            //Borrar lista de tags del servicio de antena
            ModuloAntena.Instance.BorrarListaTags();

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Abierta") + "( " + Traduccion.Traducir("Mantenimiento") + " )");
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Modo") + ": " + _turno.ModoAperturaVia.ToString() + " - " + Traduccion.Traducir("Cajero") + ": " + _operadorActual.ID.ToString());
        }

        /// <summary>
        /// Abre el programa de testeo
        /// </summary>
        private async void AbrirTesteo()
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("ABRIENDO TESTEO..."));

            //Arma evento de mantenimiento
            EventoApagadoEncendido oEvento = new EventoApagadoEncendido();
            oEvento.Movim = 'T';
            oEvento.Observaciones = "Mant. Técnico Código de Cajero: " + _turno.Operador?.ID;

            //Envía el evento de mantenimiento
            await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, _turno, oEvento);
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, null, oEvento, true);

            ModuloVideoContinuo.Instance.AbrirCerrarVideoContinuo(eComandosVideoServer.Close);
            //Espero un segundo para darle tiempo a que envíe el evento
            Thread.Sleep(1000);

            // Envia a pantalla comando para ejecutar proceso
            //ModuloPantalla.Instance.IniciarProceso( "C:\\Via\\Testeo MGO\\Testeo.exe", "Tecnico" );
            ModuloPantalla.Instance.IniciarProceso(eProcesos.Testeo);
        }

        /// <summary>
        /// Reinicia la terminal
        /// </summary>
        private async void ReiniciarTerminal()
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("REINICIANDO TERMINAL..."));

            //Arma evento de mantenimiento
            EventoApagadoEncendido oEvento = new EventoApagadoEncendido();
            oEvento.Movim = 'R';
            oEvento.Observaciones = _turno.Operador.ID;

            //Envía el evento de mantenimiento
            await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, _turno, oEvento);
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, null, oEvento, true);

            ModuloVideoContinuo.Instance.AbrirCerrarVideoContinuo(eComandosVideoServer.Close);
            //Espero un segundo para darle tiempo a que envíe el evento
            Thread.Sleep(1000);

            // Reiniciar
            //ClassUtiles.ReiniciarTerminal();
            ModuloPantalla.Instance.IniciarProceso(eProcesos.Reinicio);
        }

        /// <summary>
        /// Apaga la terminal
        /// </summary>
        private async void ApagarTerminal()
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("APAGANDO TERMINAL..."));

            //Arma evento de mantenimiento
            EventoApagadoEncendido oEvento = new EventoApagadoEncendido();
            oEvento.Movim = 'S';
            oEvento.Observaciones = _turno.Operador.ID;

            //Envía el evento de mantenimiento
            await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, _turno, oEvento);
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, null, oEvento, true);

            ModuloVideoContinuo.Instance.AbrirCerrarVideoContinuo(eComandosVideoServer.Close);
            //Espero un segundo para darle tiempo a que envíe el evento
            Thread.Sleep(1000);

            // Apagar
            //ClassUtiles.ApagarTerminal();
            ModuloPantalla.Instance.IniciarProceso(eProcesos.Apagado);
        }

        /// <summary>
        /// Cierra la sesion local
        /// </summary>
        private async void CerrarSesion()
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("CERRANDO SESIÓN..."));

            //Arma evento de mantenimiento
            EventoApagadoEncendido oEvento = new EventoApagadoEncendido();
            oEvento.Movim = 'C';
            oEvento.Observaciones = _turno.Operador.ID;

            //Envía el evento de mantenimiento
            await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, _turno, oEvento);
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
                await ModuloEventos.Instance.SetApagadoEncendido(ModuloBaseDatos.Instance.ConfigVia, null, oEvento, true);

            ModuloVideoContinuo.Instance.AbrirCerrarVideoContinuo(eComandosVideoServer.Close);
            //Espero un segundo para darle tiempo a que envíe el evento
            Thread.Sleep(1000);

            // Envia a pantalla comando para ejecutar proceso
            ModuloPantalla.Instance.IniciarProceso(eProcesos.CierreSesion);
            //ModuloPantalla.Instance.IniciarProceso( "ShutDown", "/l" );
        }

        /// <summary>
        /// Valida las precondiciones necesarias para el menu del tecnico
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesTecnico()
        {
            bool retValue = true;
            int nivelAcceso = 0;

            if (!int.TryParse(_operadorActual.NivelAcceso, out nivelAcceso))
            {
                _logger?.Warn("Error al parsear nivel acceso");

                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Nivel de acceso no válido"));
                retValue = false;
            }
            else if (nivelAcceso != (int)eNivelUsuario.Tecnico &&
                     nivelAcceso != (int)eNivelUsuario.Sistemas)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.UsuarioNoTecnico);
                retValue = false;
            }

            return retValue;
        }

        #endregion

        #region Menu Venta/Recarga

        /// <summary>
        /// Procesa el Menu de Venta con las distintas opciones posibles
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaMenuVenta(ComandoLogica comando)
        {
            if (ValidarPrecondicionesVenta())
            {
                ListadoOpciones opciones = new ListadoOpciones();

                List<enmOpcionesRecarga> listaOpcionesRecarga = new List<enmOpcionesRecarga>();
                CargarOpcionMenu(ref opciones, enmOpcionesRecarga.CobroDeuda.ToString(), true, "Cobro de Deuda", string.Empty, 2);
                listaOpcionesRecarga.Add(enmOpcionesRecarga.CobroDeuda);
                int orden = 1;

                CargarOpcionMenu(ref opciones, enmOpcionesRecarga.Recarga.ToString(), false, enmOpcionesRecarga.Recarga.ToString(), string.Empty, orden++);
                listaOpcionesRecarga.Add(enmOpcionesRecarga.Recarga);

                if (listaOpcionesRecarga.Count == 1)
                {
                    switch (listaOpcionesRecarga[0])
                    {
                        case enmOpcionesRecarga.Recarga:
                            TeclaRecarga(null);
                            break;
                            //case enmOpcionesRecarga.CobroAbono:
                            //    TeclaCobroAbono();
                            //    break;
                         case enmOpcionesRecarga.CobroDeuda:
                            RecibirCobroDeuda(null);
                            break;
                    }
                }
                else
                {
                    Causa causa = new Causa(eCausas.VentaRecarga, ClassUtiles.GetEnumDescr(eCausas.VentaRecarga));

                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                }
                /*else if (tipoComando == eTipoComando.eSeleccion ||
                         origenComando == eOrigenComando.Supervision ||
                         causaApBarrera != null)
                {
                    SubeBarrera(causaApBarrera, origenComando);
                }*/
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Valida las precondiciones necesarias para el menu de venta/recarga
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesVenta()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.VentaRecarga))
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                retValue = false;
            }
            else if (_logicaVia.EstaOcupadoLazoSalida())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                retValue = false;
            }
            else if (EsCambioJornada() && !ModoPermite(ePermisosModos.CobrarViaAbiertaJornadaAnt))
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                retValue = false;
            }

            //vuelvo a traer el veh antes de validar esta precondición
            vehiculo = _logicaVia.GetPrimerVehiculo();

            if (vehiculo.ProcesandoViolacion)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                retValue = false;
            }

            return retValue;
        }

        #endregion

        #region Recarga

        /// <summary>
        /// Recibe un string JSON con los datos correspondientes
        /// </summary>
        /// <param name="comando"></param>
        public async void TeclaRecarga(ComandoLogica comando)
        {
            _logger.Info("TeclaRecarga -> Inicio");
            //Si está leyendo finalizo
            ModuloTarjetaChip.Instance.FinalizaLectura();

            await ProcesarRecarga(null);
        }

        public async void SalvarRecarga(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Ok)
            {
                //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                if (!_ultimaAccion.MismaAccion(comando.Accion) || _ultimaAccion.AccionVencida())
                    _ultimaAccion.Clear();

                if (!_ultimaAccion.AccionEnProceso())
                {
                    _logger.Debug("SalvarRecarga -> Accion Actual: {0}", comando.Accion);
                    _ultimaAccion.GuardarAccionActual(comando.Accion);

                    Venta venta = ClassUtiles.ExtraerObjetoJson<Venta>(comando.Operacion);

                    RecargaPosible recargaPosible = new RecargaPosible();

                    //recargaPosible.Agrupacion
                    //recargaPosible.CodigoRecarga
                    recargaPosible.MontoRecarga = venta.Importe;
                    //recargaPosible.TipoCuenta

                    await ProcesarRecarga(recargaPosible);
                }
            }
            else if (comando.CodigoStatus == enmStatus.Abortada)
            {
                Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
                vehiculo.VentaAsociada = false; //Presionan ESC limpiamos flag

                if (vehiculo.EstaImpago)
                {
                    _estado = eEstadoVia.EVAbiertaLibre;
                }
                else
                {
                    _estado = eEstadoVia.EVAbiertaPag;

                    // Se actualizan perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                    _logger?.Debug("SalvarRecarga -> BARRERA ARRIBA!!");

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    //DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                    mimicos.EstadoBarrera = enmEstadoBarrera.Arriba;
                    mimicos.SemaforoPaso = eEstadoSemaforo.Verde;

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                }
            }
        }

        /// <summary>
        /// Procesa todos los pasos de comunicacion con pantalla 
        /// desde que se presiona la tecla hasta que se realiza la venta/recarga
        /// </summary>
        /// <param name="recargaPosible"></param>
        private async Task ProcesarRecarga(RecargaPosible recargaPosible)
        {
            _logger.Debug("ProcesarRecarga -> Inicio. RecargaPosible [{0}]", recargaPosible == null ? "NULL" : "NOT NULL");
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            if (ValidarPrecondicionesRecarga())
            {
                ModuloPantalla.Instance.LimpiarMensajes();

                if (recargaPosible == null)
                {
                    Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
                    ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
                    bool bHayPosibles = false;

                    vehiculo.VentaAsociada = true; //true para que no limpie el vehiculo por timeout

                    // Trae la lista de recargas posibles para la agrupación del tag asociado
                    List<RecargaPosible> listaRecargasPosibles = await ModuloBaseDatos.Instance.BuscarRecargaPosibleAsync((int)vehiculo.InfoTag.TipoTag, (int)vehiculo.InfoTag.Subfp);

                    ListadoOpciones listadoOpciones = new ListadoOpciones();
                    int orden = 1;

                    // Si existen recargas posibles
                    if (listaRecargasPosibles?.Count > 0)
                    {
                        // Para cada recarga posible, mientras el saldo del vehiculo sea mayor al monto de recarga
                        foreach (var item in listaRecargasPosibles)
                        {
                            if (item.MontoRecarga >= (vehiculo.Tarifa - vehiculo.InfoTag.SaldoInicial))
                            {
                                CargarOpcionMenu(ref listadoOpciones, JsonConvert.SerializeObject(item), true, item.MontoRecarga.ToString("0.00"), string.Empty, orden, false);
                                bHayPosibles = true;
                            }
                            orden++;
                        }
                    }

                    //busca nuevamente el vehiculo por si se movio
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    //if (vehiculo.InfoTag.TipOp != 'C')
                    vehiculo.EsperaRecargaVia = true;

                    if (vehiculo.TipoTarifa != vehiculo.InfoTag.TipoTarifa)
                        vehiculo.TipoTarifa = vehiculo.InfoTag.TipoTarifa;

                    if (!string.IsNullOrEmpty(vehiculo.HistorialCategorias) && vehiculo.EsperaRecargaVia)
                    {
                        vehiculo.Categoria = vehiculo.InfoTag.Categoria > 0 ? vehiculo.InfoTag.Categoria : vehiculo.InfoTag.CategoTabulada;
                        vehiculo.TipoDiaHora = vehiculo.InfoTag.TipDH;
                        vehiculo.Tarifa = vehiculo.InfoTag.Tarifa;
                        vehiculo.DesCatego = vehiculo.InfoTag.CategoDescripcionLarga;
                        vehiculo.CategoDescripcionLarga = vehiculo.InfoTag.CategoDescripcionLarga;

                        // Se envia a pantalla para mostrar los datos del vehiculo que indica el Tag
                        //List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                    }
                    else if (vehiculo.Tarifa != vehiculo.InfoTag.Tarifa)
                    {
                        if (!string.IsNullOrEmpty(vehiculo.TipoDiaHora))
                            vehiculo.InfoTag.TipDH = vehiculo.TipoDiaHora;
                        if (vehiculo.Tarifa > 0)
                            vehiculo.InfoTag.Tarifa = vehiculo.Tarifa;
                        if (!string.IsNullOrEmpty(vehiculo.CategoDescripcionLarga))
                            vehiculo.InfoTag.CategoDescripcionLarga = vehiculo.CategoDescripcionLarga;
                    }

                    if (ModoPermite(ePermisosModos.RecargaOtrosValores) && bHayPosibles)
                    {
                        CargarOpcionMenu(ref listadoOpciones, "*", true, "Otros Valores", string.Empty, orden++);
                    }

                    if (listadoOpciones.ListaOpciones.Any())
                    {
                        if (_estado == eEstadoVia.EVAbiertaPag)
                        {
                            _logger?.Debug("ProcesarRecarga:: eEstadoVia.EVAbiertaPag -> Bajo barrera y semaforo a rojo");
                            // Se actualizan perifericos
                            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
                            // Solo bajo la barrera si no hay nadie en el lazo de salida
                            if (!_logicaVia.EstaOcupadoBucleSalida())
                            {
                                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                                _logger?.Debug("ProcesarRecarga -> BARRERA ABAJO!!");
                            }
                            else
                            {
                                _logger.Info("ProcesarRecarga:: BARRERA NO BAJA por bucle ocupado");
                            }

                            // Actualiza el estado de los mimicos en pantalla
                            Mimicos mimicos = new Mimicos();
                            //DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                            mimicos.EstadoBarrera = enmEstadoBarrera.Abajo;
                            mimicos.SemaforoPaso = eEstadoSemaforo.Rojo;

                            listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                        }

                        listaDatosVia.Clear();

                        Causa causa = new Causa(eCausas.Recarga, eCausas.Recarga.ToString());

                        ClassUtiles.InsertarDatoVia(listadoOpciones, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.RECARGA, listaDatosVia);
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, Traduccion.Traducir("Saldo") + ": " + ClassUtiles.FormatearMonedaAString(vehiculo.InfoTag.SaldoFinal));

                        _estado = eEstadoVia.EVAbiertaVenta;
                    }
                    else if (!bHayPosibles && listaRecargasPosibles?.Count > 0)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Saldo deudor supera el máximo de recargas"));
                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron recargas posibles"));
                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                    }
                }
                else
                {
                    bool recargaValida = true;

                    Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

                    if (!vehiculo.VentaAsociada)
                        vehiculo.VentaAsociada = true;
                    // Trae la lista de recargas posibles para la agrupación del tag asociado
                    List<RecargaPosible> listaRecargasPosibles = await ModuloBaseDatos.Instance.BuscarRecargaPosibleAsync(-1, 1);

                    if (listaRecargasPosibles != null && listaRecargasPosibles.Any())
                    {
                        decimal recargaMinima = listaRecargasPosibles.Select(x => x.MontoRecarga).Min();
                        decimal recargaMaxima = listaRecargasPosibles.Select(x => x.MontoRecarga).Max();

                        if ((vehiculo.Tarifa - vehiculo.InfoTag.SaldoInicial) > recargaMinima)
                        {
                            recargaMinima = vehiculo.Tarifa - vehiculo.InfoTag.SaldoInicial;
                        }

                        if (recargaPosible.MontoRecarga < recargaMinima)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Valor a recargar debe ser mayor a ") + ClassUtiles.FormatearMonedaAString(recargaMinima));
                            recargaValida = false;
                        }
                        else if (recargaPosible.MontoRecarga > recargaMaxima)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Valor a recargar debe ser menor a ") + ClassUtiles.FormatearMonedaAString(recargaMaxima));
                            recargaValida = false;
                        }

                        if (recargaValida)
                        {
                            await RealizarRecarga(recargaPosible, vehiculo);
                        }
                        else
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron recargas posibles"));
                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                    }

                    if (vehiculo.EstaImpago)
                    {
                        _estado = eEstadoVia.EVAbiertaLibre;
                    }
                    else
                    {
                        _estado = eEstadoVia.EVAbiertaPag;

                        // Se actualizan perifericos
                        DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                        DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                        _logger?.Debug("ProcesarRecarga -> BARRERA ARRIBA!!");

                        // Se envia mensaje al display
                        ModuloDisplay.Instance.Enviar(eDisplay.REC, vehiculo);

                        // Actualiza el estado de los mimicos en pantalla
                        Mimicos mimicos = new Mimicos();
                        DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);
                        mimicos.EstadoBarrera = enmEstadoBarrera.Arriba;
                        mimicos.SemaforoPaso = eEstadoSemaforo.Verde;

                        ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                    }
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }

            _ultimaAccion.Clear();
        }

        /// <summary>
        /// Valida las precondiciones necesarias para poder realizar una venta/recarga
        /// </summary>
        /// <returns></returns>
        private bool ValidarPrecondicionesRecarga()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.VentaRecarga))
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.Tag))
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (vehiculo.InfoTag.LecturaManual == 'S' && !ModoPermite(ePermisosModos.RecargaTagManual))
                {
                    if (!_logicaVia.UltimoAnuladoEsIgual(vehiculo.InfoTag.Patente))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                        retValue = false;
                    }
                }
                else if (vehiculo.Categoria <= 0)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado);
                    retValue = false;
                }
                else if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Nada)
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoTag);
                    retValue = false;
                }
                else if (vehiculo.TipOp == 'C' && !vehiculo.EstaPagado && vehiculo.InfoTag?.ErrorTag == eErrorTag.NoError)
                {
                    retValue = false;
                }
                else if (vehiculo.InfoTag.TipoTag == eTipoCuentaTag.Ufre)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TagNoPrepago);
                    retValue = false;
                }

                else if (vehiculo.ListaRecarga.Any(x => x != null))
                {
                    ModuloPantalla.Instance.LimpiarMensajes();
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoYaPoseeRecarga);
                    retValue = false;
                }
                else if (_logicaVia.EstaOcupadoLazoSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (EsCambioJornada())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                    retValue = false;
                }

                //vuelvo a traer el veh antes de validar esta precondición
                vehiculo = _logicaVia.GetPrimerVehiculo();

                if (vehiculo.ProcesandoViolacion)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                    retValue = false;
                }
            }
            return retValue;
        }

        private async Task RealizarRecarga(RecargaPosible recargaPosible, Vehiculo vehiculo)
        {
            _logger.Info("RealizarRecarga -> Vehiculo[{0}] Tag[{1}] Pat[{2}]", vehiculo.NumeroVehiculo, vehiculo.InfoTag.NumeroTag, vehiculo.InfoTag.Patente);
            if (ValidarPrecondicionesRecarga())
            {
                ulong ulNroVehiculo = vehiculo.NumeroVehiculo;
                // Se busca la tarifa
                TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, vehiculo.InfoTag.TipoTarifa);
                Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                vehiculo.TipoVenta = eVentas.R;
                vehiculo.CategoriaDesc = tarifa?.Descripcion;

                // NO se modifica la hora del vehiculo, que es cuando se cobró. La fecha actual va en el objeto recarga más adelante
                //vehiculo.Fecha = Fecha;
                vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);


                // La fecha fiscal es importante para generar correctamente la clave de acceso NO intercambiar el orden.
                vehiculo.FechaFiscal = DateTime.Now;
                vehiculo.Fecha = DateTime.Now;
                vehiculo.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);
                vehiculo.TipoRecarga = recargaPosible.CodigoRecarga;
                decimal dAux = vehiculo.InfoTag.SaldoFinal;
                decimal dTarifaux = vehiculo.Tarifa;

                //se busca el numero de cliente solo si aun no lo tiene
                if (vehiculo.InfoCliente == null || vehiculo.InfoCliente.Clave == 0)
                {
                    InfoCliente cliente = new InfoCliente();
                    cliente.Clave = vehiculo.InfoTag.NroCliente;
                    vehiculo.InfoCliente = cliente;
                }

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                if (vehiculo.ListaRecarga.Count() == 0 && !vehiculo.EstaPagado)
                {
                    //el saldo final reflejará el monto total descontando la tarifa
                    vehiculo.InfoTag.SaldoFinal = vehiculo.InfoTag.SaldoInicial + recargaPosible.MontoRecarga - vehiculo.Tarifa;
                    //en el ticket se muestra el monto de la recarga, no el de la tarifa
                    vehiculo.Tarifa = recargaPosible.MontoRecarga;
                }
                else
                {
                    //si el cliente solo quiere hacer una recarga despues de pasar con saldo
                    vehiculo.InfoTag.SaldoInicial = vehiculo.InfoTag.SaldoFinal;
                    //suma monto de la recarga al saldo final para mostrar en el ticket
                    vehiculo.InfoTag.SaldoFinal += recargaPosible.MontoRecarga;
                    //como ya está pago no se muestra la tarifa de la categoria, si no la de la recarga
                    vehiculo.Tarifa = recargaPosible.MontoRecarga;
                }

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                    && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    ModuloPantalla.Instance.LimpiarMensajes();

                    //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                    vehiculo.Tarifa = 0;
                    vehiculo.InfoTag.SaldoFinal = dAux;
                    vehiculo.TipoVenta = eVentas.Nada;

                    //vehiculo.NoPermiteTag = false;
                    //vehiculo.CobroEnCurso = false;
                    //vehiculo.TipOp = ' ';
                    if (_esModoMantenimiento)
                    {
                        vehiculo.NumeroTicketNF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    }
                    else
                    {
                        vehiculo.NumeroTicketF = 0;
                        ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                        _logger.Debug($"RealizarRecarga -> Decrementa nro ticket. Actual [{ulNro}]");
                    }
                    //Indico el error de la impresora al usuario
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);

                    List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                }
                else
                {
                    if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                        && ModoPermite(ePermisosModos.TransitoSinImpresora))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                    }

                    Recarga recarga = new Recarga();
                    recarga.NumeracionFiscal = new NumeracionFiscal();

                    // Se envia el evento de recarga correspondiente
                    vehiculo.Tarifa = recargaPosible.MontoRecarga;

                    //Genero el ticket legible
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.RecargaTag, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;

                    //Se incrementa tránsito si no tenía nro
                    if (vehiculo.NumeroTransito == 0)
                        vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                    recarga.TicketLegible = vehiculo.TicketLegible;
                    recarga.ClaveAcceso = vehiculo.ClaveAcceso;
                    recarga.Monto = recargaPosible.MontoRecarga;
                    recarga.Patente = vehiculo.Patente;
                    recarga.TipoTarifa = vehiculo.TipoTarifa;
                    recarga.Abortada = 'N';
                    recarga.Cuenta = (ulong)vehiculo.InfoTag.Cuenta;
                    recarga.FormaPago = 'E';
                    recarga.FechaRecarga = Fecha;
                    recarga.Manua = (byte)vehiculo.Categoria;
                    recarga.NumeracionFiscal.FechaFiscal = vehiculo.FechaFiscal;// Fecha;
                    recarga.NumeracionFiscal.PuntoVenta = ModuloBaseDatos.Instance.ConfigVia.Get_NumeroPuntoVta();
                    recarga.NumeroTransito = vehiculo.NumeroTransito;
                    recarga.NumeracionFiscal.NumeroTicket = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketNoFiscal);
                    recarga.Salmo = vehiculo.InfoTag.SaldoInicial + recarga.Monto;

                    vehiculo.ListaRecarga.Add(recarga);
                    vehiculo.InfoTag.RecargaReciente = 'S';
                    //vehiculo.EsperaRecargaVia = false;                    

                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, string.Format(Traduccion.Traducir("Se realizó una carga por {0} - Saldo Final: {1}"),
                    ClassUtiles.FormatearMonedaAString(recargaPosible.MontoRecarga), ClassUtiles.FormatearMonedaAString(vehiculo.InfoTag.SaldoFinal)));

                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, string.Format(Traduccion.Traducir("Patente") + ": {0}", vehiculo.InfoTag.Patente));

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());

                    _logger.Debug("RealizarRecarga -> Se agrega recarga al vehiculo. Nro. [{0}] Recargas: [{1}] Transito[{2}]", vehiculo.NumeroVehiculo, vehiculo.ListaRecarga.Count(), vehiculo.NumeroTransito);

                    _loggerTransitos?.Info($"R;{DateTime.Now.ToString("HH:mm:ss.ff")};{vehiculo.Categoria};{recargaPosible.MontoRecarga};{recarga.NumeracionFiscal.NumeroTicket};{vehiculo.Patente};{vehiculo.InfoTag.NumeroTag};{vehiculo.InfoTag.Ruc};1");

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    await ModuloBaseDatos.Instance.AlmacenarVentaTurnoAsync(vehiculo, recargaPosible);
                    ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.RecargaAVI);
                    ModuloEventos.Instance.SetRecargaCobro(_turno, recarga, ModuloBaseDatos.Instance.ConfigVia, vehiculo);
                    {
                        ImprimiendoTicket = true;
                        errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.RecargaTag, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia);
                        ImprimiendoTicket = false;
                    }

                    if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                        _logger.Info("RealizarRecarga -> Impresion OK");
                    else
                    {
                        _logger.Info("RealizarRecarga -> Impresion ERROR [{0}]", errorImpresora.ToString());
                    }

                    //Vuelvo a buscar el vehiculo
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);
                    vehiculo.Tarifa = dTarifaux;

                    if (!vehiculo.EstaPagado)
                    {
                        vehiculo.EsperaRecargaVia = false;
                        //GAB: comento porque si se le asigna forma de pago no va a encontrar el veh para asignar el tag
                        //if (vehiculo.InfoTag.PagoEnViaPrepago)
                        //  vehiculo.FormaPago = eFormaPago.CTChPrepago;
                        //no esta pagado, hay que devolver el saldo final y sumar recarga (esto es sin la aplicacion de la tarifa) porque va a procesarLecturaTag
                        vehiculo.InfoTag.SaldoFinal = dAux + recargaPosible.MontoRecarga;
                        vehiculo.InfoTag.SaldoInicial = vehiculo.InfoTag.SaldoFinal;

                        _estado = eEstadoVia.EVAbiertaCat;
                        //El vehiculo no tenia saldo
                        vehiculo.TipoRecarga = 3;

                        Tag tag = new Tag();

                        tag.NumeroTag = vehiculo.InfoTag.NumeroTag;
                        tag.NumeroTID = vehiculo.InfoTag.Tid;
                        //Cobrar la pasada con el saldo, se vuelve al procesar el tag con el nuevo saldo
                        eTipoLecturaTag tipo = vehiculo.InfoTag.TipOp == 'C' ? eTipoLecturaTag.Chip : eTipoLecturaTag.Manual;
                        _logicaVia.ProcesarLecturaTag(eEstadoAntena.Ok, tag, tipo, null, vehiculo);
                        //Vuelvo el saldo final al anterior para mostrarlo correctamente en display y pantalla
                        vehiculo.InfoTag.SaldoFinal = vehiculo.InfoTag.SaldoInicial - vehiculo.Tarifa;
                    }
                    else
                    {
                        vehiculo.InfoTag.SaldoInicial = vehiculo.InfoTag.SaldoFinal;
                        //El vehiculo tenia saldo
                        vehiculo.TipoRecarga = 2;
                        _estado = eEstadoVia.EVAbiertaCat;
                    }

                    _logicaVia.GrabarVehiculos();
                    vehiculo.VentaAsociada = false; //limpiamos flag
                    vehiculo.EsperaRecargaVia = false;

                    List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        #endregion

        #region Cancelar Recarga

        public void CancelarRecarga(ComandoLogica comando)
        {
            Causa causa = new Causa(eCausas.CancelarRecarga, ClassUtiles.GetEnumDescr(eCausas.CancelarTagYRecarga));


            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
            List<DatoVia> listaDatosVia = new List<DatoVia>();

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);

            

        }

        public void ConfirmarCancelacionRecarga(ref Vehiculo vehiculo)
        {
            RecargaPosible recargaPos = new RecargaPosible();

            //Modifica las recargas a Abortadas = 'S'
            foreach (var recarga in vehiculo.ListaRecarga)
            {
                if (recarga != null)
                {
                    //Cambio a estado Abortada todas las recargas
                    recarga.Abortada = 'S';

                    //Descontar el valor de la recarga del saldo del tag/ chip
                    vehiculo.InfoTag.SaldoFinal = (long)(vehiculo.InfoTag.SaldoInicial - recarga.Monto);

                    //Sumar recarga cancelada en total de turnos
                    recargaPos.MontoRecarga = recarga.Monto;
                    ModuloBaseDatos.Instance.AlmacenarVentaAnuladaTurnoAsync(vehiculo, recargaPos);
                    ModuloEventos.Instance.SetRecargaFinal(_turno, recarga, ModuloBaseDatos.Instance.ConfigVia, vehiculo);
                }
            }

            //almacena anomalia de cancelación recarga
            ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.CanRecarga);

            //await ProcesarCancelacion( eTipoComando.eTecla, null );
        }

        #endregion

        #region Vecino/Viaje

        public override async void TeclaUnViaje(ComandoLogica comando)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
            if (!_ultimaAccion.MismaAccion(enmAccion.T_UNVIAJE) || _ultimaAccion.AccionVencida())
                _ultimaAccion.Clear();

            if (!_ultimaAccion.AccionEnProceso())
            {
                _logger.Debug("TeclaUnViaje -> Accion Actual: {0}", enmAccion.T_UNVIAJE);
                _ultimaAccion.GuardarAccionActual(enmAccion.T_UNVIAJE);

                await ProcesarUnViaje(vehiculo, eOrigenComando.Pantalla);
            }
        }

        /// <summary>
        /// Realiza el pago de los prepagos pago en via llamando al metodo pagadoenvia
        /// </summary>
        private async Task ProcesarUnViaje(Vehiculo vehiculo, eOrigenComando origenComando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            vehiculo.InfoTag.TipOp = 'T';
            vehiculo.InfoTag.TipBo = 'U';

            if (await ValidarPrecondicionesUnViaje())
            {
                if (vehiculo != null)
                {
                    if (!string.IsNullOrEmpty(vehiculo.HistorialCategorias))
                    {
                        vehiculo.EsperaRecargaVia = true;
                        vehiculo.Categoria = vehiculo.InfoTag.Categoria > 0 ? vehiculo.InfoTag.Categoria : vehiculo.InfoTag.CategoTabulada;
                        vehiculo.TipoDiaHora = vehiculo.InfoTag.TipDH;
                        vehiculo.Tarifa = vehiculo.InfoTag.Tarifa;
                        vehiculo.DesCatego = vehiculo.InfoTag.CategoDescripcionLarga;
                        vehiculo.CategoDescripcionLarga = vehiculo.InfoTag.CategoDescripcionLarga;

                        // Se envia a pantalla para mostrar los datos del vehiculo que indica el Tag
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                    }

                    await PagadoEnVia();
                }
            }
            else
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
            _ultimaAccion.Clear();
        }

        public async Task<bool> ValidarPrecondicionesUnViaje()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetVehIng();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaCerrada);
                retValue = false;
            }
            else
            {
                if (!ModoPermite(ePermisosModos.Tag))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.VentaRecarga))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else
                {
                    if (vehiculo.EstaPagado)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);
                        retValue = false;
                    }
                    else if (vehiculo.InfoTag?.TipoSaldo != 'V' && vehiculo.InfoTag.PagoEnVia != 'S')
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPagoEnVia);
                        retValue = false;
                    }
                    else
                    {
                        bool categoFPagoValida = await CategoriaFormaPagoValida('O', vehiculo.InfoTag.TipBo, (byte)vehiculo.Categoria);

                        if (!categoFPagoValida)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CategoriaNoCorrespondeFormaPago);
                            retValue = false;
                        }
                    }

                    if (retValue && _logicaVia.EstaOcupadoSeparadorSalida()) //_logicaVia.EstaOcupadoLazoSalida())
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                        retValue = false;
                    }
                    else if (vehiculo.EsperaRecargaVia && vehiculo.InfoTag.TipoTag != eTipoCuentaTag.Ufre && vehiculo.InfoTag.TipOp != 'C' && !string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                    {
                        if ((vehiculo.InfoTag.LecturaManual == 'S' && vehiculo.TipoVenta == eVentas.Nada) && !vehiculo.InfoTag.TagOK)
                            return retValue;
                        else if (vehiculo.TipoVenta != eVentas.Nada)
                        {
                            retValue = false;
                            return retValue;
                        }

                        if (ModoPermite(ePermisosModos.TagPagoViaOtrasFormas) && !vehiculo.InfoTag.PagoEnViaPrepago)
                        {
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EsperaRecargaTag);
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.OtraFormaPago);

                            retValue = false;
                        }
                    }
                    else if (EsCambioTarifa(false))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                        retValue = false;
                    }
                    else if (EsCambioJornada())
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                        retValue = false;
                    }

                    //vuelvo a traer el veh antes de validar esta precondición
                    vehiculo = _logicaVia.GetPrimerVehiculo();

                    if (vehiculo.ProcesandoViolacion)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                        retValue = false;
                    }
                }
            }

            return retValue;
        }

        #endregion

        #region Opciones Impresora

        private async Task ProcesarOpcionImpresora(eOpcionesImpresora opcionesImpresora)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            switch (opcionesImpresora)
            {
                case eOpcionesImpresora.Status:
                    if (VerificarStatusImpresora(true))
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Impresora OK"));
                    break;

                case eOpcionesImpresora.ReimprimirEncabezado:
                    await ReimprimirEncabezado();
                    break;

                case eOpcionesImpresora.CierreZ:
                    break;
                default:
                    break;
            }
            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            else
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
        }

        private bool VerificarStatusImpresora(bool MuestraMsgPantalla)
        {
            bool retValue = true;

            if (ValidarPrecondicionesStatusImpresora())
            {
                if (MuestraMsgPantalla)
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Chequeando status impresora..."));

                ImprimiendoTicket = true;

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

                ImprimiendoTicket = false;

                if (errorImpresora == EnmErrorImpresora.PocoPapel)
                {
                    if (MuestraMsgPantalla)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                }
                else if (errorImpresora != EnmErrorImpresora.SinFalla)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                    retValue = false;
                }
                else
                {
                    if (MuestraMsgPantalla)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Impresora OK"));
                }
            }

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            if (ModuloPantalla.Instance._ultimaPantalla == enmAccion.T_CATEGORIAS)
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
            else
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);

            return retValue;
        }

        private bool ValidarPrecondicionesStatusImpresora()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo();

            if (vehiculo.EstaPagado)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                retValue = false;
            }

            return retValue;
        }

        private async Task ReimprimirEncabezado()
        {
            if (ValidarPrecondicionesImprEncabezado())
            {
                ModuloImpresora.Instance.UltimoImprimeCabeceraCola = false;
                await ImprimirEncabezado();
            }
        }

        private async Task ImprimirEncabezado(bool mostrarMensaje = false)
        {
            if (mostrarMensaje)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Imprimiendo Encabezado..."));

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                ImprimiendoTicket = true;

                errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(false, enmFormatoTicket.Cabecera, null, null, ModuloBaseDatos.Instance.ConfigVia);

                ImprimiendoTicket = false;
            }

            if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
            }
        }

        private bool ValidarPrecondicionesImprEncabezado()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else if (_modo.Cajero != "S")
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoSinCajero);
                retValue = false;
            }
            else if (vehiculo.EstaPagado)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                retValue = false;
            }

            return retValue;
        }

        #endregion

        #region ProcesarConfirmacion
        /// <summary>
        /// Procesa accion CONFIRMAR desde pantalla
        /// </summary>
        /// <param name="comando"></param>
        public async void ProcesarConfirmacion(ComandoLogica comando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            try
            {
                Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);

                _logger?.Debug("ProcesarConfirmacion -> causa[{0}]", causa.Descripcion);

                switch (causa.Codigo)
                {
                    case eCausas.CancelarTag:
                        ConfirmarCancelacionTagPago(comando);
                        break;

                    case eCausas.ReincioViaFaltaLlave:
                        ReiniciarTerminal();
                        //CerrarSesion();
                        break;

                    case eCausas.BajarBarrera:
                        ProcesarBajaBarrera(eTipoComando.eTecla, eOrigenComando.Pantalla);
                        break;

                    case eCausas.CancelarRecarga:
                        await ProcesarCancelacion(eTipoComando.eTecla, null);
                        break;

                    case eCausas.FinalizarQuiebre:
                        await FinQuiebre(eOrigenComando.Pantalla);
                        break;

                    case eCausas.AperturaTurno:
                    case eCausas.AperturaTurnoSupervisor:
                        {
                            Modos modo = ClassUtiles.ExtraerObjetoJson<Modos>(comando.Operacion);
                            Operador operador = ClassUtiles.ExtraerObjetoJson<Operador>(comando.Operacion);

                            await AperturaTurno(modo, operador, false, eOrigenComando.Pantalla);
                        }
                        break;

                    case eCausas.AperturaTurnoMantenimiento:
                        {
                            Modos modo = ClassUtiles.ExtraerObjetoJson<Modos>(comando.Operacion);
                            await AperturaModoMantenimiento(modo);
                        }
                        break;
                    case eCausas.CambiarFormaPago:
                        {
                            ComandoLogica com = ClassUtiles.ExtraerObjetoJson<ComandoLogica>(comando.Operacion);

                            if (com.Accion == enmAccion.T_ESCAPE)
                            {
                                Vehiculo oVehiculo = _logicaVia.GetPrimerVehiculo();

                                if (oVehiculo.EsperaRecargaVia)
                                    oVehiculo.EsperaRecargaVia = false;

                                oVehiculo.TipoTarifa = 0;
                                // Actualiza el estado de vehiculo en pantalla
                                List<DatoVia> listaDatosVia = new List<DatoVia>();
                                ClienteDB clienteDB = new ClienteDB();
                                Vehiculo vehiculo = oVehiculo;
                                vehiculo.Patente = "";
                                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                                ClassUtiles.InsertarDatoVia(clienteDB, ref listaDatosVia);
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                            }
                            else if (com.Accion == enmAccion.T_CANCELAR)
                                EliminarTagPago();
                        }
                        break;
                    case eCausas.PagoTarjetaChip:
                        {
                            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
                            if (!_ultimaAccion.MismaAccion(enmAccion.CONFIRMAR) || _ultimaAccion.AccionVencida())
                                _ultimaAccion.Clear();

                            if (!_ultimaAccion.AccionEnProceso())
                            {
                                _logger.Debug("ProcesarConfirmacion -> Accion Actual: {0}", enmAccion.CONFIRMAR);
                                _ultimaAccion.GuardarAccionActual(enmAccion.CONFIRMAR);

                                // Si no es TipOp = C es porque se categorizó nuevamente, para cambiar la forma de pago o leer otra tarjeta
                                if (_logicaVia.GetVehIng().TipOp == 'C')
                                    await PagadoTarjetaChip();
                            }
                        }
                        break;
                    case eCausas.SimulacionPaso:
                        //está el bucle de salida ocupado y el cajero igualmente quiere realizar SIP
                        {
                            _bSIPforzado = true;
                            await ProcesarSimulacion(eTipoComando.eTecla, eOrigenComando.Pantalla, null);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                _loggerExcepciones.Error(e);
            }

        }
        #endregion

        #region Cancelacion Tag Pago
        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA ESC
        /// </summary>
        /// <param name="comando"></param>
        public override void CancelarTagPago(ComandoLogica comando)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            Causa causa = new Causa(eCausas.CancelarTag, ClassUtiles.GetEnumDescr(eCausas.CancelarTag));

            List<DatoVia> listaDatosVia = new List<DatoVia>();

            ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
        }

        public void ConfirmarCancelacionTagPago(ComandoLogica comando)
        {
            EliminarTagPago();
        }

        private void EliminarTagPago()
        {
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            // Si el vehiculo tiene un tag asignado
            if (!string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
                _logicaVia.EliminarTagManualD(vehiculo.InfoTag.NumeroTag);

            ModuloPantalla.Instance.LimpiarVehiculo(null);
            ModuloDisplay.Instance.Enviar(eDisplay.BNV);
        }

        #endregion

        #region Numeracion

        private void ProcesarNumeracion(/*Opcion opcionSeleccionada,*/Operador operador/*, Numeracion numeracion*/)
        {
            ModuloPantalla.Instance.LimpiarMensajes();
            Numeracion numeracion = new Numeracion();

            try
            {
                numeracion.NumeroTurno = (ulong)_numeracion.InformacionTurno.NumeroTurno;
                numeracion.NumeroTransito = _numeracion.Contadores.Values.Where(f => f.TipoNumerador == eContadores.NumeroTransito).Select(f => f.ValorFinal).FirstOrDefault();
                numeracion.Boleta = _numeracion.Contadores.Values.Where(f => f.TipoNumerador == eContadores.NumeroTicketFiscal).Select(f => f.ValorFinal).FirstOrDefault().ToString();
                numeracion.Factura = _numeracion.Contadores.Values.Where(f => f.TipoNumerador == eContadores.NumeroFactura).Select(f => f.ValorFinal).FirstOrDefault().ToString();
                numeracion.OrigenDatos = _numeracion.UltimoTurno ? "ESTACION" : "BASE DE DATOS LOCAL";
                numeracion.Detraccion = _numeracion.Contadores.Values.Where(f => f.TipoNumerador == eContadores.NumeroDetraccion).Select(f => f.ValorFinal).FirstOrDefault();

                if (ValidarPrecondicionesNumeracion(operador, numeracion))
                {
                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(numeracion, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(operador, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.NUMERACION, listaDatosVia);

                    ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.NumeracionSinConfirmar, 1);
                }
                else
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.FallaCritica, enmAccion.ESTADO_SUB, null);
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }
        }

        private bool ValidarPrecondicionesNumeracion(Operador operador, Numeracion numeracion)
        {
            bool retValue = true;

            if (_estado != eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoCerrada);
                retValue = false;
            }
            else if (_estadoNumeracion == eEstadoNumeracion.Ninguno || _estadoNumeracion == eEstadoNumeracion.SinNumeracion)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaSinNumeracion);
                retValue = false;
            }

            int nivelAcceso = 0;

            if (retValue && !int.TryParse(_operadorActual.NivelAcceso, out nivelAcceso))
            {
                _logger?.Warn("Error al parsear nivel acceso");
                retValue = false;
            }
            else if (nivelAcceso != (int)eNivelUsuario.Tecnico)
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.UsuarioNoTecnico);
                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Consulta la numeración local/estación
        /// </summary>
        /// <param name="operador"></param>
        /// <param name="bMostrarLocal"></param>
        private void ConsultarNumeracion(Operador operador, bool bMostrarLocal = true)
        {
            ModuloPantalla.Instance.LimpiarMensajes();
            Numeracion numeracion = new Numeracion();
            EnmStatusBD estado = EnmStatusBD.OK;

            if (bMostrarLocal)
            {
                numeracion.NumeroTurno = _turno.NumeroTurno;
                numeracion.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                numeracion.Boleta = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal).ToString();
                numeracion.Factura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura).ToString();
                numeracion.OrigenDatos = "BASE DE DATOS LOCAL";
                numeracion.Consulta = true;
                numeracion.Detraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);

                List<DatoVia> listaDatosVia = new List<DatoVia>();

                ClassUtiles.InsertarDatoVia(numeracion, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(operador, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.NUMERACION, listaDatosVia);
            }
            else
            {
                UltTurno oUlt = ModuloBaseDatos.Instance.ConsultarUltimoTurno(ref estado);

                if (estado == EnmStatusBD.OK)
                {
                    numeracion.NumeroTurno = (ulong)oUlt.NumeroTurno;
                    numeracion.NumeroTransito = (ulong)oUlt.UltimoTransito;
                    numeracion.Boleta = oUlt.UltimoTicketFiscal.ToString();
                    numeracion.Factura = oUlt.UltimaFactura.ToString();
                    numeracion.OrigenDatos = "ESTACION";
                    numeracion.Consulta = true;
                    numeracion.Detraccion = (ulong)oUlt.UltimaDetraccion;

                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(numeracion, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(operador, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.NUMERACION, listaDatosVia);
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error en consulta de numeración, reintente"));
            }
        }

        /// <summary>
        /// Actualiza la numeracion de la BD local con datos del servidor, y queda pendiente de autorización
        /// </summary>
        private void ActualizarNumeracion(ComandoLogica comando)
        {
            Numeracion numr = ClassUtiles.ExtraerObjetoJson<Numeracion>(comando.Operacion);
            bool bConsultar = numr.OrigenDatos == "BASE DE DATOS LOCAL" ? true : false;

            if (bConsultar)
                ConsultarNumeracion(_operadorActual, false);
            else
            {
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Actualizando la numeración local..."), true);

                var task = Task.Run(async () => (await ModuloBaseDatos.Instance.ObtenerUltimoTurnoAsync(false)));
                Vars oVarsEst = task.Result;

                if (oVarsEst == null || oVarsEst.Contadores?.Count == 0)
                {
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error en la actualización, reintente"));
                }
                else
                {
                    SetEstadoNumeracion(eEstadoNumeracion.NumeracionSinConfirmar, oVarsEst);
                    ModuloBaseDatos.Instance.ActualizarEstadoNumeracion(_estadoNumeracion);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);
                    //Envio a pantalla el ultimo numero de transito y factura
                    Vehiculo veh = new Vehiculo();
                    veh.NumeroTransito = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTransito);
                    veh.NumeroTicketF = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
                    veh.NumeroFactura = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);
                    veh.NumeroDetraccion = ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroDetraccion);
                    veh.FormaPago = eFormaPago.Nada;
                    ModuloPantalla.Instance.LimpiarVehiculo(veh);
                }
            }
        }
        #endregion

        #region Apertura Supervisor
        private async Task ProcesarMenuSupervisor(eAperturasSupervisor aperturaSupervisor)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            _logger.Info("ProcesarMenuSupervisor -> [{0}]", aperturaSupervisor.ToString());

            if (ValidarPrecondicionesSupervisor())
            {
                switch (aperturaSupervisor)
                {
                    case eAperturasSupervisor.AbrirVia:

                        //ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);
                        await EnviaModos(null, _operadorActual, eOrigenComando.Pantalla, eNivelUsuario.Supervisor);
                        break;

                    case eAperturasSupervisor.ImpresionTotales:

                        ImprimirTotales(null);
                        break;

                    case eAperturasSupervisor.ImpresionTotalesOtroTurno:

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);

                        List<DatoVia> listaDatosVia = new List<DatoVia>();

                        Causa causa = new Causa(eCausas.ViaEstacion, eCausas.ViaEstacion.GetDescription());

                        ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Tecla, enmAccion.VIAESTACION, listaDatosVia);

                        break;
                }
            }
        }

        public override async void ImprimirTotales(ComandoLogica comando)
        {
            enmStatus statRes = enmStatus.Error;
            ViaEstacion viaEstacion = null;
            ConfigVia oConfig = ModuloBaseDatos.Instance.ConfigVia;

            // Si los totales a imprimir son de otra via
            if (comando != null)
            {
                viaEstacion = ClassUtiles.ExtraerObjetoJson<ViaEstacion>(comando.Operacion);
                oConfig.CodigoEstacion = byte.Parse(viaEstacion.Estacion);
            }

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(false, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                TotalesTransitoTurno totalesTransitoTurno = await ModuloBaseDatos.Instance.ObtenerTotalesTransitoAsync(viaEstacion);

                ImprimiendoTicket = true;

                if (totalesTransitoTurno.EstadoConsulta == EnmStatusBD.OK)
                {
                    _turno.TotalesTransitoTurno = totalesTransitoTurno;

                    errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.Totales, null, _turno, oConfig, null);

                    if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Se imprimieron los Totales"));
                        statRes = enmStatus.Ok;
                    }
                    else
                    {
                        //Indico el error de la impresora al usuario
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                    }
                }
                else if (totalesTransitoTurno.EstadoConsulta == EnmStatusBD.TIMEOUT)
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Timeout de la consulta"));
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se encontraron totales") + ":");

                ImprimiendoTicket = false;
            }
            else
                //Indico el error de la impresora al usuario
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));

            //Indico a pantalla que cierre o no la sub ventana
            ModuloPantalla.Instance.EnviarDatos(statRes, enmAccion.ESTADO_SUB, null);
        }

        private bool ValidarPrecondicionesSupervisor()
        {
            bool retValue = true;

            return retValue;
        }

        #endregion

        #region Procesamiento de Credenciales
        /// <summary>
        /// Si se encuentra el ID recibido por pantalla en la base, 
        /// se comparan los password y se procede a validar condiciones para apertura de turno
        /// </summary>
        /// <param></param>
        private async Task<Operador> ValidarCredenciales(Login operadorPantalla)
        {
            Operador operador = new Operador();
            if (string.IsNullOrEmpty(operadorPantalla.PasswordViejo))
                operador = ModuloBaseDatos.Instance.ObtenerOperadorEnLinea(operadorPantalla.Usuario);
            else
                operador.Contrasena = "-1";

            // No se pudo realizar la consulta al servidor
            if (operador.Contrasena == "-1")
            {
                operador = new Operador();
                //Envio consulta de usuario a la base local
                operador = await ModuloBaseDatos.Instance.ObtenerOperadorBDLocal(operadorPantalla.Usuario);
            }

            // Se verifican las credenciales
            if (operador == null ||
                operadorPantalla.Usuario?.ToUpper() != operador.ID?.ToUpper() ||
                operadorPantalla.Password != operador.Contrasena)
            {
                operador = null;
            }

            return operador;
        }

        /// <summary>
        /// Valida las credenciales recibidas por el modulo de pantalla con el modulo de base de datos
        /// </summary>
        /// <param name="comandoJson"></param>
        public override async void ProcesarCredenciales(ComandoLogica comandoJson)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            try
            {
                Login login = ClassUtiles.ExtraerObjetoJson<Login>(comandoJson.Operacion);
                Causa causaRecibida = ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                if (comandoJson.CodigoStatus == enmStatus.Ok)
                {
                    Operador operador = await ValidarCredenciales(login);

                    // Si son validas las credenciales
                    if (operador != null)
                    {
                        // Si el password es vacío o esta vencido
                        if (login.PasswordVacia || operador.FechaVencimiento < Fecha)
                        {
                            _causaLogin = causaRecibida;

                            login.PasswordViejo = login.Password;
                            login.Vencimiento = operador.FechaVencimiento.GetValueOrDefault();

                            if (login.PasswordVacia && operador.ValidezContrasenaBlanco < Fecha)
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PasswordVencido);
                            else
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NuevoPassword);

                                Causa causa = new Causa();

                                causa.Codigo = eCausas.CambioPassword;
                                causa.Descripcion = Traduccion.Traducir(ClassUtiles.GetEnumDescr(eCausas.CambioPassword));

                                List<DatoVia> listaDatosVia = new List<DatoVia>();

                                ClassUtiles.InsertarDatoVia(login, ref listaDatosVia);
                                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                                // La pantalla muestra la ventana para ingresar nuevo password
                                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CAMBIO_PASS, listaDatosVia);
                            }
                        }
                        else
                        {
                            SetOperadorActual(operador);

                            if (causaRecibida.Codigo == eCausas.AperturaTurno)
                                await ProcesarAperturaTurno(eTipoComando.eConfirmacion, null, eOrigenComando.Pantalla, operador, causaRecibida.Codigo);
                            else if (causaRecibida.Codigo == eCausas.Retiro)
                                await ProcesarTeclaRetiro(operador);
                            else if (causaRecibida.Codigo == eCausas.Quiebre)
                                await ProcesarInicioQuiebre(operador, eOrigenComando.Pantalla);
                        }
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CredencialesErroneas);

                        ModuloEventos.Instance.SetCredencialesErroneas(login);

                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                    }

                }
                else if (comandoJson.CodigoStatus == enmStatus.Abortada)
                { }
            }
            catch (JsonException jsonEx)
            {
                _loggerExcepciones?.Error(jsonEx);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="comandoJson"></param>
        public override async void ValidarCambioPassword(ComandoLogica comandoJson)
        {
            try
            {
                Login login = ClassUtiles.ExtraerObjetoJson<Login>(comandoJson.Operacion);
                Causa causaRecibida = ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                if (comandoJson.CodigoStatus == enmStatus.Ok)
                {
                    // Genera evento de cambio de contraseña
                    ModuloEventos.Instance.SetNuevoPass(_turno, login);

                    // Actualizar contraseña en BD
                    bool graboNuevoPassword = await ModuloBaseDatos.Instance.GrabarNuevoPasswordOperadorAsync(login);

                    if (graboNuevoPassword)
                    {
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);

                        login.PasswordVacia = false;

                        List<DatoVia> listaDatosVia = new List<DatoVia>();

                        ClassUtiles.InsertarDatoVia(_causaLogin, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(login, ref listaDatosVia);

                        ComandoLogica comando = new ComandoLogica(enmStatus.Ok, enmAccion.LOGIN, JsonConvert.SerializeObject(listaDatosVia));

                        ProcesarCredenciales(comando);
                        _causaLogin = new Causa();

                        ////Causa causa = new Causa( eCausas.CambioPassword, ClassUtiles.GetEnumDescr( eCausas.CambioPassword ) );
                        //Causa causa = new Causa(eCausas.AperturaTurno, ClassUtiles.GetEnumDescr(eCausas.AperturaTurno));

                        //// Si hay mas de un modo de apertura manual, no solicito confirmar login
                        //Opcion opcionApertura = new Opcion();
                        //List<Modos> listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);
                        //if (listaModos?.Count(x => x.Cajero == "S") > 0)
                        //{
                        //    opcionApertura.Confirmar = false;
                        //}
                        //else
                        //{
                        //    opcionApertura.Confirmar = true;
                        //}

                        //List<DatoVia> listaDatosVia = new List<DatoVia>();
                        //ClassUtiles.InsertarDatoVia( causa, ref listaDatosVia );
                        //ClassUtiles.InsertarDatoVia(opcionApertura, ref listaDatosVia);

                        //// Puede abrir turno - Se solicita nuevamente usuario y password
                        //ModuloPantalla.Instance.EnviarDatos( enmStatus.Ok, enmAccion.LOGIN, listaDatosVia );
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No pudo grabarse nuevo password"));
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Confirme nuevamente"));
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Error, enmAccion.ESTADO_SUB, null);
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Abortada)
                {
                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        #endregion

        #region Resultados de Consultas a BD Local
        public void ResultadoConsultaBD(EnmStatusBD estado, eTablaBD consulta)
        {
            if (estado != EnmStatusBD.OK)
            {
                if (consulta == eTablaBD.CodigoCancelacion)
                {
                    if (estado == EnmStatusBD.SINRESULTADO || estado == EnmStatusBD.ERRORBUSQUEDA)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de cancelacion"));
                    else if (estado == EnmStatusBD.TIMEOUT)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error al realizar consulta"));
                        _logger?.Debug("Consulta [{0}] -> TIMEOUT", consulta.ToString());
                    }
                }
                else if (consulta == eTablaBD.Modo)
                {
                    if (estado == EnmStatusBD.SINRESULTADO || estado == EnmStatusBD.ERRORBUSQUEDA)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No existen modos de apertura"));
                    else if (estado == EnmStatusBD.TIMEOUT)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error al realizar consulta"));
                        _logger?.Debug("Consulta [{0}] -> TIMEOUT", consulta.ToString());
                    }
                }
                else if (consulta == eTablaBD.CodigoCierre)
                {
                    if (estado == EnmStatusBD.SINRESULTADO || estado == EnmStatusBD.ERRORBUSQUEDA)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de cierre"));
                    else if (estado == EnmStatusBD.TIMEOUT)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error al realizar consulta"));
                        _logger?.Debug("Consulta [{0}] -> TIMEOUT", consulta.ToString());
                    }
                }
                else if (consulta == eTablaBD.CodigoSimulacionPaso)
                {
                    if (estado == EnmStatusBD.SINRESULTADO || estado == EnmStatusBD.ERRORBUSQUEDA)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No existen causas de simulacion"));
                    else if (estado == EnmStatusBD.TIMEOUT)
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error al realizar consulta"));
                        _logger?.Debug("Consulta [{0}] -> TIMEOUT", consulta.ToString());
                    }
                }
            }
        }
        #endregion

        #region Timers

        ///// <summary>
        ///// Obtiene la fecha actual cada 1 segundo
        ///// </summary>
        ///// <param name="source"></param>
        ///// <param name="e"></param>
        private void Timer1Segundo(object source, ElapsedEventArgs e)
        {
            //Cada 1 segundo
            Fecha = DateTime.Now;
            TimerRevisaCambioJornada();
            EstadoViaSinTransito();
        }

        /// <summary>
        /// Actualiza el numerador de la fecha para verificar luego de un reinicio
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void TimerChequeoTimeStamp(object source, ElapsedEventArgs e)
        {
            ModuloBaseDatos.Instance.ActualizarTimestamp(Fecha);
        }

        private void OnTimedEventEstadoImp(object source, ElapsedEventArgs e)
        {
            if (_estado == eEstadoVia.EVCerrada)
                ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);
        }

        private void OnTimedEventEstadoOnline(object source, ElapsedEventArgs e)
        {
            _timerEstadoOnline?.Stop();
            UpdateOnline();
            TimerRevisaCambioRunner();
            SinEspacioEnDisco();
            _timerEstadoOnline?.Start();
        }

        private void OnTimedEventServicioMonitor(object source, ElapsedEventArgs e)
        {
            EnviarServicioMonitor();
        }

        private void TimerMsgCambioJornada(object source, ElapsedEventArgs e)
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornadaProximo);
        }

        private void OnTimedEventConsultaPesos(object source, ElapsedEventArgs e)
        {
            if (!_reintentandoConsulta)
            {
                _reintentandoConsulta = true;
                if (ModuloBaseDatos.Instance.HayDatosLocales)
                {
                    List<Simpesos> lPesos = ModuloBaseDatos.Instance.BuscarSimPesos();

                    if (lPesos.Any())
                    {
                        _timerConsultaPesos?.Stop();
                        _logger.Debug("Tengo la lista de SIMPESOS, son : {0}", lPesos.Count);

                        ClassUtiles.DistanciaLevenshtein_CargarPesos(lPesos);
                    }
                    else
                        _reintentandoConsulta = false;
                }
                else
                    _reintentandoConsulta = false;
            }
        }

        #endregion

        #region Metodos relacionados con el Monitoreo de Servicios

        private void EnviarServicioMonitor()
        {
            if (ModuloMonitor.Instance.ListaEstadoServicios.Any())
            {
                // Si el turno esta cerrado no monitoreo los servicios para evitar reinicios constantes
                bool monitorear = _estado != eEstadoVia.EVCerrada ? true : false;
                ModuloMonitor.Instance.MonitorearServicio("Display", monitorear);
                ModuloMonitor.Instance.MonitorearServicio("Foto", monitorear);
                ModuloMonitor.Instance.MonitorearServicio("Video", monitorear);
                ModuloMonitor.Instance.MonitorearServicio("VideoContinuo", monitorear);

                ModuloMonitor.Instance.MonitorearServicio("Chip", _modo?.Cajero == "S" && monitorear &&
                                                           ModuloBaseDatos.Instance.ConfigVia?.TChip == "S");
                ModuloMonitor.Instance.MonitorearServicio("Antena", monitorear && ModuloBaseDatos.Instance.ConfigVia?.Telepeaje == "S");
                ModuloMonitor.Instance.MonitorearServicio("Impresora", monitorear && _modo?.Cajero == "S");

                ModuloMonitor.Instance.MonitorearServicio("OCR", monitorear && ModuloBaseDatos.Instance.ConfigVia?.TieneOCR == 'S');

                ModuloMonitor.Instance.Enviar(true);
            }
        }

        public void AntenaStatusComando(eEstadoAntena status, byte numeroSensor)
        {
            ModuloMonitor.Instance.IncrementarDictionary("Antena", (int)status);
        }

        public void ImpresoraStatusComando(EnmErrorImpresora respuesta, byte numeroSensor, Turno turno, ConfigVia configVia, Vehiculo vehiculo)
        {
            try
            {
                ModuloMonitor.Instance.IncrementarDictionary("Impresora", (int)respuesta);
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        public void EventosFoto(eEstadoFoto status, byte numeroSensor, Foto oFoto, Vehiculo vehiculo)
        {
            ModuloMonitor.Instance.IncrementarDictionary("Foto", (int)status);
        }

        public void VideoStatusComando(eEstadoVideo status, byte numeroSensor, Video oVideo, Vehiculo vehiculo)
        {
            ModuloMonitor.Instance.IncrementarDictionary("Video", (int)status);
        }

        public void EventosDisplay(eEstadoDisplay respuesta)
        {
            ModuloMonitor.Instance.IncrementarDictionary("Display", (int)respuesta);
        }

        public void EventosChip(eStatusChip status, string sObservacion)
        {
            ModuloMonitor.Instance.IncrementarDictionary("Chip", (int)status);
        }

        #endregion

        #region Teclas sin implementar      

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA ENTER
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaEnter(ComandoLogica comando)
        {

        }

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA MONEDA
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaMoneda(ComandoLogica comando)
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "TECLA MONEDA NO IMPLEMENTADA");
        }

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA QUIEBRE BARRERA
        /// </summary>
        /// <param name="comando"></param>
        public override void TeclaQuiebreBarrera(ComandoLogica comando)
        {
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "TECLA QUIEBRE BARRERA NO IMPLEMENTADA");
        }

        #endregion

        #region Comandos desde Supervision

        /// <summary>
        /// Procesa comando recibido de supervisión
        /// </summary>
        /// <param name="oComando">Información del Comando</param>
        private async void ProcesarComandoSupervision(Comandos oComando)
        {
            //el Codigo indica el comando enviado desde supervision
            //el Parametro indica información extra para ejecutar ese comando
            //switch a los diferentes comandos, agrego unos que estaban en via vieja como ejemplo(Corredor Panamericano)

            if (oComando == null)
                return;

            // Se agrega el id del comando recibido a una lista para luego responderlo correctamente
            _comandosSupervision.Add(oComando.ID);

            eComandosSupervision comandoSupervision;
            if (!Enum.TryParse(oComando.Codigo, out comandoSupervision))
            {
                _logger?.Warn("Error al parsear comandoSupervision");
            }
            else
            {
                _logger?.Debug("LogicaCobroManual::ProcesarComandoSupervision -> NumeroVia[{0}] - comandoSupervision[{1}]", oComando.NumeroVia, comandoSupervision.ToString());
                //Si controlo via de escape, compruebo si el comando es para mi o la de escape.
                if (ModuloBaseDatos.Instance.ConfigVia.HasEscape() && oComando.NumeroVia == ModuloBaseDatos.Instance.ConfigVia.NumeroViaEscape)
                {
                    // Abrir Barrera
                    if (comandoSupervision == eComandosSupervision.ABBA)
                    {
                        AbrirCerrarBarreraEscape(true);
                        // Respuesta exitosa del comando al cliente grafico
                        ResponderComandoSupervision("E", "Ejecutado con éxito", true);
                    }
                    else
                        ResponderComandoSupervision("X", "No se puede ejecutar en vía de escape", true);
                }
                else
                {
                    switch (comandoSupervision)
                    {
                        // Abrir Barrera
                        case eComandosSupervision.ABBA:
                            await ProcesarSubeBarrera(eTipoComando.eSeleccion, eOrigenComando.Supervision, null);
                            break;

                        // Semáforo de Marquesina
                        case eComandosSupervision.SEMA:

                            eEstadoSemaforo estadoSemaforo = oComando.Parametro == "V" ? eEstadoSemaforo.Verde : eEstadoSemaforo.Rojo;

                            ProcesarSemaforoMarquesina(estadoSemaforo, eOrigenComando.Supervision, eCausaSemaforoMarquesina.Tecla);
                            break;

                        // Abrir la Vía
                        case eComandosSupervision.ABVI:

                            if (oComando.Parametro.Contains("|"))
                            {
                                string[] stringArray = oComando.Parametro.Split('|');

                                Modos modo = new Modos();
                                Operador operador = new Operador();

                                if (!string.IsNullOrEmpty(stringArray[0]))
                                {
                                    modo.Modo = stringArray[0];

                                    if (!string.IsNullOrEmpty(stringArray[1]))
                                    {
                                        operador = ModuloBaseDatos.Instance.BuscarOperadorPrivate(stringArray[1]);

                                        if (operador == null)
                                        {
                                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": Operador Inexistente");
                                            ResponderComandoSupervision("X", "Operador inexistente");
                                        }
                                        else
                                        {
                                            if (modo.Modo != "D")
                                                await ProcesarAperturaTurno(eTipoComando.eSeleccion, modo, eOrigenComando.Supervision, operador, eCausas.AperturaTurno);
                                            else
                                                await ProcesarAperturaAutomatica(modo, eOrigenComando.Supervision);
                                        }
                                    }
                                    else
                                    {
                                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": No se pudo obtener el operador");
                                        ResponderComandoSupervision("X", "No se pudo obtener el operador");
                                    }
                                }
                                else
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": No se pudo obtener el modo");
                                    ResponderComandoSupervision("X", "No se pudo obtener el modo");
                                }
                            }
                            else
                            {
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": Error de formato de parametro");
                                ResponderComandoSupervision("X", "Error de formato de parametro");
                            }

                            break;

                        // Cerrar la Vía
                        case eComandosSupervision.CEVI:

                            CausaCierre causaCierre = new CausaCierre();
                            causaCierre.Codigo = ((int)eCodigoCierre.FinalTurno).ToString();
                            await ProcesarCierreTurno(eTipoComando.eSeleccion, eOrigenComando.Supervision, causaCierre);
                            break;

                        // Mensajes
                        case eComandosSupervision.MENS:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.MsgSupRecibido, Fecha.ToString("HH:mm:ss") + " - " + oComando.Parametro);
                            ResponderComandoSupervision("E");
                            break;

                        // Quiebre de Barrera
                        case eComandosSupervision.QUIE:

                            // Iniciar quiebre liberado
                            if (oComando.Parametro == "I")
                            {
                                Runner runner = await ModuloBaseDatos.Instance.BuscarRunnerActualAsync();

                                if (runner == null)
                                {
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Iniciar Quiebre") + ": No se pudo obtener runner");
                                    ResponderComandoSupervision("X", "No se pudo obtener runner");
                                }
                                else
                                {
                                    if (_turno.Operador == null)
                                        _turno.Operador = new Operador();

                                    if (_turno.Parte == null)
                                        _turno.Parte = new Parte();

                                    // Asigno los datos del supervisor al parte
                                    _turno.Parte.IDCajero = runner.IDSupervisor;
                                    _turno.Parte.NombreCajero = runner.IDSupervisor;
                                    _turno.Operador = await ModuloBaseDatos.Instance.BuscarOperadorAsync(runner.IDSupervisor);

                                    SetOperadorActual(_turno.Operador);

                                    await ProcesarInicioQuiebre(_operadorActual, eOrigenComando.Supervision);
                                }
                            }
                            // Finalizar quiebre liberado
                            else if (oComando.Parametro == "F")
                                await ProcesarFinQuiebre(eOrigenComando.Supervision);
                            else
                                ResponderComandoSupervision("X", "Error, parametro " + oComando.Parametro + " desconocido");

                            // SI se utilizaran los dos tipos de quiebre (controlado y liberado)
                            //if( oComando.Parametro.Contains( "|" ) )
                            //{
                            //    string[] stringArray = oComando.Parametro.Split( '|' );

                            //    if( !string.IsNullOrEmpty( stringArray[1] ) )
                            //    {
                            //        // Quiebre Liberado
                            //        if( stringArray[1] == "QL" )
                            //        {
                            //            if( !string.IsNullOrEmpty( stringArray[0] ) )
                            //            {
                            //                // Iniciar
                            //                if( stringArray[0] == "I" )
                            //                {
                            //                    await ProcesarInicioQuiebre( _operadorActual, eOrigenComando.Supervision );
                            //                }
                            //                // Finalizar
                            //                else if( stringArray[0] == "F" )
                            //                {
                            //                    await ProcesarFinQuiebre( eOrigenComando.Supervision );
                            //                }
                            //            }
                            //        }
                            //        // Quiebre Controlado
                            //        else if( stringArray[1] == "QC" )
                            //        {
                            //            if( !string.IsNullOrEmpty( stringArray[0] ) )
                            //            {
                            //                // Iniciar
                            //                if( stringArray[0] == "I" )
                            //                {
                            //                    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, "PANAVIAL NO IMPLMENTA QUIEBRE CONTROLADO" );
                            //                }
                            //                // Finalizar
                            //                else if( stringArray[0] == "F" )
                            //                {
                            //                    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, "PANAVIAL NO IMPLMENTA QUIEBRE CONTROLADO" );
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            break;

                        // Habilitar Exento
                        case eComandosSupervision.HEXE:
                            //COD|PATENTE|---
                            if (oComando.Parametro.Contains("|"))
                            {
                                string[] stringArray = oComando.Parametro.Split('|');

                                // Por lo menos codigo y patente
                                if (stringArray.Count() >= 2)
                                {
                                    PatenteExenta patenteExenta = new PatenteExenta();

                                    patenteExenta.TipoExento = int.Parse(stringArray[0]);
                                    patenteExenta.Patente = stringArray[1].ToUpper();

                                    Exento tipoExento = new Exento();
                                    tipoExento.Codigo = int.Parse(stringArray[0]);

                                    await ProcesarExento(eTipoComando.eValidacion, eOrigenComando.Supervision, tipoExento, patenteExenta);
                                }
                                else
                                    ResponderComandoSupervision("X", "Error de parametrización");
                            }
                            else
                                ResponderComandoSupervision("X", "Error de parametrización");

                            break;

                        // Habilitar Tag
                        case eComandosSupervision.HTAG:
                            //PATENTE|---|NRO_TAG|CAUSA|---
                            //KZR055 |---| 000000000000000094001840 | 95 | ---

                            if (oComando.Parametro.Contains("|"))
                            {
                                string[] stringArray = oComando.Parametro.Split('|');

                                if (stringArray.Count() >= 4)
                                {
                                    Tag tag = new Tag();
                                    Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
                                    tag.NumeroTag = stringArray[2];
                                    tag.NumeroTID = vehiculo.InfoTag.Tid;

                                    await ProcesarTagManual(stringArray[0].ToUpper(), tag, eOrigenComando.Supervision);
                                }
                                else
                                    ResponderComandoSupervision("X", "Error de parametrización");
                            }
                            else
                                ResponderComandoSupervision("X", "Error de parametrización");

                            break;

                        // Habilitación Forzada
                        case eComandosSupervision.LIFO:
                            //PATENTE|--- (CAUSA?)
                            if (oComando.Parametro.Contains("|"))
                            {
                                string[] stringArray = oComando.Parametro.Split('|');

                                if (!string.IsNullOrEmpty(stringArray[0]))
                                {
                                    Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();
                                    vehiculo.Patente = stringArray[0].ToUpper();

                                    CausaAperturaBarrera causaApBarrera = new CausaAperturaBarrera();

                                    // El 4 indica "Autoriza Supervisor"
                                    causaApBarrera.Codigo = "4"; // TODO ver

                                    await ProcesarSubeBarrera(eTipoComando.eSeleccion, eOrigenComando.Supervision, causaApBarrera);
                                }
                                else
                                    ResponderComandoSupervision("X", "Error de parametrización");
                            }
                            else
                                ResponderComandoSupervision("X", "Error de parametrización");

                            break;



                        // Abrir Barrera Salida
                        case eComandosSupervision.ABSA:
                            break;

                        // Limpiar Alarmas
                        case eComandosSupervision.LIAL:
                            break;

                        // Cartel de Marquesina
                        case eComandosSupervision.MARQ:
                            break;

                        // Modo Sin Antena
                        case eComandosSupervision.MOSA:
                            break;

                        // Modo Tandem
                        case eComandosSupervision.MOTA:
                            break;

                        // Quiebre Controlado
                        case eComandosSupervision.QCON:
                            break;

                        // Quiebre Liberado
                        case eComandosSupervision.QLIB:
                            break;

                        // Habilitar Redondeo
                        case eComandosSupervision.REDO:
                            break;

                        // Sacar Vehículo
                        case eComandosSupervision.SACA:
                            break;

                        // Actualizar Foto
                        case eComandosSupervision.FOTO:
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Arma el comando de respuesta y envia el evento
        /// </summary>
        /// <param name="idComando"></param>
        /// <param name="status"></param>
        /// <param name="observacion"></param>
        private void ResponderComandoSupervision(string status, string observacion = "Ejecutado con éxito", bool esViaEscape = false)
        {
            UpdateComandos updateComando = new UpdateComandos();

            try
            {
                updateComando.FechaEjecucion = Fecha;
                updateComando.ID = _comandosSupervision[0];
                _comandosSupervision.RemoveAt(0);
                updateComando.Status = status;
                updateComando.Observacion = observacion;
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }

            ModuloEventos.Instance.UpdateComandos(updateComando, esViaEscape);
        }

        #endregion

        #region Apertura Automatica

        private async Task ProcesarAperturaAutomatica(Modos modo, eOrigenComando origenComando)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            if (await ValidarPrecondicionesAperturaAutomatica(origenComando))
            {
                List<Modos> listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);

                // Si tengo más un posible modo(sin cajero) de apertura 
                if (modo == null && listaModos?.Count(x => x.Cajero == "N") > 0)
                {
                    ListadoOpciones opciones = new ListadoOpciones();

                    int orden = 1;

                    // Para cada modo posible de apertura sin cajero, genero una opcion a mostrar en pantalla
                    foreach (Modos modoAux in listaModos.Where(x => x.Cajero == "N"))
                    {
                        CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(modoAux), false, modoAux.Descripcion, string.Empty, orden++, false);
                    }

                    // Genero la causa de apertura
                    Causa causaApertura = new Causa();
                    causaApertura.Codigo = eCausas.AperturaAutomatica;
                    causaApertura.Descripcion = ClassUtiles.GetEnumDescr(eCausas.AperturaAutomatica);

                    opciones.MuestraOpcionIndividual = false;

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(causaApertura, ref listaDatosVia);

                    // Se envia lista de causas posibles de apertura
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
                }
                else
                {
                    Runner runner = await ModuloBaseDatos.Instance.BuscarRunnerActualAsync();

                    if (runner == null)
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se pudo obtener runner"));
                    else
                    {
                        if (_turno.Operador == null)
                            _turno.Operador = new Operador();

                        // Asigno los datos del supervisor a _parte, 
                        // para que despues la pantalla pueda obtenerlos
                        _turno.Parte.IDCajero = runner.IDSupervisor;
                        _turno.Parte.NombreCajero = runner.IDSupervisor;
                        _turno.Operador = await ModuloBaseDatos.Instance.BuscarOperadorAsync(runner.IDSupervisor);
                        _turno.Parte.NumeroParte = 0; //para no enviar el evento de SetApertura con el parte anterior (si la via estaba abierta antes)

                        SetOperadorActual(_turno.Operador);

                        // No es necesario enviar a pantalla la lista de modos 
                        //para apertura porque solo hay uno
                        if (modo == null && listaModos != null && listaModos.Any())
                        {
                            modo = listaModos[listaModos.FindIndex(x => x.Cajero == "N")];
                        }

                        // Abrimos turno
                        await AperturaModoAutomatico(modo, runner, origenComando);

                        //Consulto el parte
                        await ConsultarParte(runner);
                    }
                }
            }
        }

        private async Task<bool> ValidarPrecondicionesAperturaAutomatica(eOrigenComando origenComando)
        {
            bool retValue = true;

            if (!IsNumeracionOk())
            {
                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaNoInicializada.GetDescription() + eMensajesPantalla.VerificarNumeracion.GetDescription());
                else
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.VerificarNumeracion);
                }

                retValue = false;
            }
            else if (_estado != eEstadoVia.EVCerrada)
            {
                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaNoCerrada.GetDescription());
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoCerrada);

                retValue = false;
            }
            else if ((DAC_PlacaIO.Instance.EntradaBidi() /*&& SentidoUltimoTransito*/ ))
            {
                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", eMensajesPantalla.ViaOpuestaAbierta.GetDescription());
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaOpuestaAbierta);

                retValue = false;
            }
            else if (SinEspacioEnDisco())
                retValue = false;

            List<Modos> listaModos = await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia);

            if (retValue && listaModos != null && !listaModos.Any(x => x.Cajero == "N"))
            {
                if (origenComando == eOrigenComando.Supervision)
                    ResponderComandoSupervision("X", eMensajesPantalla.ModeloNoPermiteModo.GetDescription());
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModeloNoPermiteModo);

                retValue = false;
            }

            if (retValue)
            {
                Runner runner = await ModuloBaseDatos.Instance.BuscarRunnerActualAsync();

                if (runner == null)
                {
                    if (origenComando == eOrigenComando.Supervision)
                        ResponderComandoSupervision("X", "No se pude obtener runner");
                    else
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No se pudo obtener runner"));

                    retValue = false;
                }
                else
                {
                    TimeSpan timeSpan = Fecha - runner.FechaInicioTurno;

                    if (retValue && !ModoPermite(ePermisosModos.RunnerObsoleto) && timeSpan.TotalHours >= 24)
                    {
                        if (origenComando == eOrigenComando.Supervision)
                            ResponderComandoSupervision("X", eMensajesPantalla.RunnerObsoleto.GetDescription());
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.RunnerObsoleto);
                        retValue = false;
                    }
                }
            }

            return retValue;
        }

        private async Task AperturaModoAutomatico(Modos modo, Runner runnerActual, eOrigenComando origenComando, bool EnviarSetApertura = true)
        {
            _estado = eEstadoVia.EVAbiertaLibre;

            SetModo(modo);

            AsignarModoATurno();

            // Actualiza las variables necesarios de Turno
            _turno.Modo = _modo.Modo;
            _turno.Mantenimiento = 'N';
            _turno.EstadoTurno = enmEstadoTurno.Abierta;
            _turno.FechaApertura = EnviarSetApertura ? Fecha : _turno.FechaApertura;
            ulong auxLong = await ModuloBaseDatos.Instance.ObtenerNumeroTurnoAsync();
            _turno.NumeroTurno = auxLong == 0 ? _turno.NumeroTurno : auxLong;
            _turno.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido == 'A' ? enmSentido.Ascendente : enmSentido.Descendente;
            _turno.Ticket = (int)ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroTicketFiscal);
            _turno.Factura = (int)ModuloBaseDatos.Instance.BuscarValorFinalContador(eContadores.NumeroFactura);

            _turno.OrigenApertura = 'R';
            _turno.PcSupervision = 'N';

            ulong idOperador = 0;

            if (!ulong.TryParse(_operadorActual?.ID, out idOperador))
            {
                _logger?.Warn("Error al parsear id operador");
            }

            _turno.NumeroTarjeta = idOperador;
            _turno.ModoQuiebre = 'N';
            _turno.Parte.NombreCajero = _operadorActual.Nombre;
            _turno.Parte.IDCajero = _operadorActual.ID;
            _turno.Operador.ID = runnerActual.IDSupervisor;

            // Se actualiza el estado de las salidas
            DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Rojo);
            DAC_PlacaIO.Instance.SalidaBidi(eEstadoBidi.Activada);

            if (ModoPermite(ePermisosModos.CerrarBarreraAlAbrir))
            {
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Baja, eTipoBarrera.Via);
                _logger?.Debug("AperturaModoAutomatico -> BARRERA ABAJO!!");
            }

            if (ModoPermite(ePermisosModos.SemMarquesinaVerdeAlAbrir))
                DAC_PlacaIO.Instance.SemaforoMarquesina(eEstadoSemaforo.Verde);

            Mimicos mimicos = new Mimicos();

            DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

            //ModuloDisplay.Instance.Enviar(eDisplay.BNV);

            ModuloAntena.Instance.ActivarAntena();

            // Obtiene el vehiculo correspondiente
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            // Actualiza estado de turno en pantalla
            listaDatosVia = new List<DatoVia>();

            ClassUtiles.InsertarDatoVia(_turno, ref listaDatosVia);
            ClassUtiles.InsertarDatoVia(ModuloBaseDatos.Instance.ConfigVia, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_TURNO, listaDatosVia);

            // Actualiza estado de vehiculo en pantalla
            listaDatosVia = new List<DatoVia>();
            vehiculo.NumeroTicketF = (ulong)_turno.Ticket;
            vehiculo.NumeroFactura = (ulong)_turno.Factura;
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
            vehiculo.NumeroTicketF = 0; //se limpia numero de ticket
            vehiculo.NumeroFactura = 0; //se limpia numero de ticket

            ModuloPantalla.Instance.CerrarSubventanas(eCausaCiereSubVen.AbrirVia);

            //Para limpiar el flag de sentido opuesto, sino la vía no genera violaciones.
            _logicaVia.UltimoSentidoEsOpuesto = false;

            // Se almacena turno en la BD local y Se envia el evento de apertura de bloque
            if (EnviarSetApertura)
            {
                await ModuloBaseDatos.Instance.AlmacenarTurnoAsync(_turno);
                ModuloEventos.Instance.SetAperturaBloque(ModuloBaseDatos.Instance.ConfigVia, _turno);
            }

            // Establecer Modelo DAC
            IniciarTimerSeteoDAC();

            //Inicio el timer de chequeo de cambio de tarifa
            _timerControlCambioTarifa.Start();

            // Resetear contadores
            // Se encarga el modulo de base de datos

            if (ModoPermite(ePermisosModos.Autotabular))
                await Categorizar((short)ModuloBaseDatos.Instance.ConfigVia.MonoCategoAutotab);

            //Si se detecta cambio de Runner ( inmediato o programado) 
            //la via cambiará el bloque al nuevo Runner

            //En los horarios de cambio de turno la via cambiará el bloque 
            //sin cambiar de Runner (pudiendo cambiar de parte )

            //Borrar lista de tags del servicio de antena
            ModuloAntena.Instance.BorrarListaTags();

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Vía Abierta"));
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Modo") + ": " + _turno.ModoAperturaVia.ToString() + " - " + Traduccion.Traducir("Cajero") + ": " + _operadorActual.ID.ToString());

            if (origenComando == eOrigenComando.Supervision)
            {
                ResponderComandoSupervision("E");
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.CmdSupervision, Fecha.ToString("HH:mm:ss") + " - " + Traduccion.Traducir("Abrir Vía") + ": OK");
            }

            _logger?.Info("Apertura de Turno realizada. Modo: {0}. Operador: {1} AperturaAutomatica: S", _turno?.ModoAperturaVia.ToString(), _operadorActual?.ID);
            _loggerTransitos?.Info($"A;{DateTime.Now.ToString("HH:mm:ss.ff")};{_operadorActual?.ID};{_turno?.ModoAperturaVia};{_turno?.Mantenimiento};{_turno?.ModoQuiebre}");
        }

        #endregion

        #region Metodos varios

        private void UpdateOnline()
        {
            // La primera vez tarda más, entonces que no espere al thread
            _logicaVia.ActualizarEstadoSensores(ModuloAlarmas.Instance.FirstOnlineSent);
            ModuloEventos.Instance.ActualizarVehiculoOnline(_logicaVia.GetVehOnline());

            if (!ModuloAlarmas.Instance.FirstOnlineSent)
            {
                ModuloAlarmas.Instance.EnviarPosiblesSensores();
                _timerEstadoOnline.Start();
            }

            ModuloEventos.Instance.SetEstadoOnline();

            //Si controlo via de escape, actualizo sensores
            if (ModuloBaseDatos.Instance.ConfigVia.HasEscape())
            {
                _logicaVia.ActualizarEstadoSensoresEscape();
                ModuloEventos.Instance.SetEstadoOnline(true);
            }
        }

        /// <summary>
        /// Busca el vehiculo nuevamente para confirmar que no se ha movido de la fila
        /// </summary>
        /// <param name="ulNroVehiculo"></param>
        /// <param name="vehiculo"></param>
        private void ConfirmarVehiculo(ulong ulNroVehiculo, ref Vehiculo vehiculo, bool bPrimerVeh = true)
        {
            bool esPrimero = false;
            if (ulNroVehiculo > 0)
            {
                vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(ulNroVehiculo, false, true));

                if (vehiculo.NumeroVehiculo != ulNroVehiculo)
                {
                    if (bPrimerVeh)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                    else
                        vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);
                }
            }
            else
            {
                if (bPrimerVeh)
                    vehiculo = _logicaVia.GetPrimerVehiculo();
                else
                    vehiculo = _logicaVia.GetPrimeroSegundoVehiculo(out esPrimero);
            }
        }

        private void SetModo(Modos modo)
        {
            if (_modo == null)
                _modo = new Modos();

            _modo = modo;

            ModuloBaseDatos.Instance.BuscarListaPermisos(modo.Modo);
            _logger.Debug("SetModo -> Se vuelve a buscar la lista de permisos");
            //ModuloBaseDatos.Instance.SetModo(modo.Modo);
            ModuloBaseDatos.Instance.ModoVia = modo.Modo;
        }

        private void SetOperadorActual(Operador operador)
        {
            if (_operadorActual == null)
                _operadorActual = new Operador();

            _operadorActual = operador;
        }

        private bool IsNumeracionOk()
        {
            return _estadoNumeracion == eEstadoNumeracion.NumeracionOk;
        }

        /// <summary>
        /// Recibe un string JSON con los datos relativos a la TECLA TURNO
        /// </summary>
        /// <param name="comando"></param>
        public override async void TeclaAperturaCierreTurno(ComandoLogica comando)
        {
            //Si no está procesando la misma acción significa que no pudo limpiar bien la acción anterior
            //Si ya pasó mucho tiempo desde la última acción limpiamos
            if (!_ultimaAccion.MismaAccion(enmAccion.T_TURNO) || _ultimaAccion.AccionVencida())
                _ultimaAccion.Clear();

            if (!_ultimaAccion.AccionEnProceso())
            {
                _ultimaAccion.GuardarAccionActual(enmAccion.T_TURNO);

                if (!_init)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoInicializada);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir(ClassUtiles.GetEnumDescr(_estadoValidacionVia)));
                }
                else
                {
                    switch (_estado)
                    {
                        case eEstadoVia.EVAbiertaLibre:
                        case eEstadoVia.EVAbiertaCat:
                        case eEstadoVia.EVQuiebreBarrera:
                            await ProcesarCierreTurno(eTipoComando.eTecla, eOrigenComando.Pantalla, null);
                            break;

                        case eEstadoVia.EVCerrada:
                            // Se limpian los mensajes de lineas
                            ModuloPantalla.Instance.LimpiarMensajes();
                            // Se limpia el vehiculo en pantalla
                            List<DatoVia> listaDatosVia = new List<DatoVia>();
                            ClassUtiles.InsertarDatoVia(new Vehiculo(), ref listaDatosVia);
                            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);
                            await ProcesarAperturaTurno(eTipoComando.eTecla, null, eOrigenComando.Pantalla, null, eCausas.AperturaTurno);
                            break;

                        case eEstadoVia.EVAbiertaPag:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Operación no permitida"));
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.ExistenVehiculosPagados);
                            break;

                        case eEstadoVia.EVAbiertaVenta:
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Operación no permitida"));
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.EnVenta);
                            break;
                    }
                }
                _ultimaAccion.Clear();
            }
        }

        /// <summary>
        /// Procesa la opcion elegida por el usuario dependiendo de la causa de seleccion
        /// </summary>
        /// <param name="comandoJson"></param>
        public override async void ProcesarOpcionMenu(ComandoLogica comandoJson)
        {
            ModuloPantalla.Instance.LimpiarMensajes();

            try
            {
                Opcion opcionSeleccionada = ClassUtiles.ExtraerObjetoJson<Opcion>(comandoJson.Operacion);
                Causa causaRecibida = ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                if (comandoJson.CodigoStatus == enmStatus.Ok)
                {
                    switch (causaRecibida.Codigo)
                    {
                        case eCausas.AperturaTurno:
                            Modos modoApertura = JsonConvert.DeserializeObject<Modos>(opcionSeleccionada.Objeto);

                            await ProcesarAperturaTurno(eTipoComando.eSeleccion, modoApertura, eOrigenComando.Pantalla, _operadorActual, causaRecibida.Codigo);

                            break;

                        case eCausas.AperturaTurnoMantenimiento:

                            modoApertura = JsonConvert.DeserializeObject<Modos>(opcionSeleccionada.Objeto);

                            await AperturaModoMantenimiento(modoApertura, true, true);

                            break;

                        case eCausas.AperturaTurnoSupervisor:

                            modoApertura = JsonConvert.DeserializeObject<Modos>(opcionSeleccionada.Objeto);

                            await AperturaTurno(modoApertura, _operadorActual, false, eOrigenComando.Pantalla, true, true);

                            break;

                        case eCausas.CausaCierre:
                            CausaCierre causaCierre = JsonConvert.DeserializeObject<CausaCierre>(opcionSeleccionada.Objeto);

                            await ProcesarCierreTurno(eTipoComando.eSeleccion, eOrigenComando.Pantalla, causaCierre);

                            break;

                        case eCausas.CausaCancelacion:
                            CausaCancelacion causaCancelacion = JsonConvert.DeserializeObject<CausaCancelacion>(opcionSeleccionada.Objeto);

                            await ProcesarCancelacion(eTipoComando.eSeleccion, causaCancelacion);

                            break;

                        case eCausas.CausaSimulacion:
                            CausaSimulacion causaSimulacion = JsonConvert.DeserializeObject<CausaSimulacion>(opcionSeleccionada.Objeto);

                            await ProcesarSimulacion(eTipoComando.eSeleccion, eOrigenComando.Pantalla, causaSimulacion);

                            break;

                        case eCausas.CausaSubeBarrera:
                            CausaAperturaBarrera causaSubeBarrera = JsonConvert.DeserializeObject<CausaAperturaBarrera>(opcionSeleccionada.Objeto);

                            await ProcesarSubeBarrera(eTipoComando.eSeleccion, eOrigenComando.Pantalla, causaSubeBarrera);

                            break;

                        case eCausas.TipoExento:
                            Exento tipoExento = JsonConvert.DeserializeObject<Exento>(opcionSeleccionada.Objeto);
                            PatenteExenta patenteExenta = ClassUtiles.ExtraerObjetoJson<PatenteExenta>(comandoJson.Operacion);

                            await ProcesarExento(eTipoComando.eConfirmacion, eOrigenComando.Pantalla, tipoExento, patenteExenta);

                            break;

                        case eCausas.Observacion:
                            Observacion observacion = JsonConvert.DeserializeObject<Observacion>(opcionSeleccionada.Objeto);

                            await ProcesarObservacion(eTipoComando.eSeleccion, observacion);

                            break;

                        case eCausas.ObservacionViol:
                            ObservacionViol observacionViol = JsonConvert.DeserializeObject<ObservacionViol>(opcionSeleccionada.Objeto);

                            await ProcesarObservacionViolacion(eTipoComando.eSeleccion, observacionViol);

                            break;

                        case eCausas.Menu:
                            await ProcesarMenu(eTipoComando.eSeleccion, opcionSeleccionada);

                            break;

                        case eCausas.MensajeSupervision:
                            MensajeSupervision mensajeSupervision = JsonConvert.DeserializeObject<MensajeSupervision>(opcionSeleccionada.Objeto);

                            await ProcesarMensajeASupervision(eTipoComando.eSeleccion, mensajeSupervision);

                            break;

                        case eCausas.MensajesDetraccion:
                            MensajesDetraccion mensajeDetraccion = JsonConvert.DeserializeObject<MensajesDetraccion>(opcionSeleccionada.Objeto);

                            await ProcesarDetraccion(eTipoComando.eSeleccion, mensajeDetraccion);

                            break;

                        case eCausas.Recarga:
                            RecargaPosible recargaPosible = JsonConvert.DeserializeObject<RecargaPosible>(opcionSeleccionada.Objeto);

                            await ProcesarRecarga(recargaPosible);

                            break;

                        case eCausas.OpcionesTecnico:
                            eOpcionesTecnico opcionAperturaTecnico;

                            opcionAperturaTecnico = (eOpcionesTecnico)Enum.Parse(typeof(eOpcionesTecnico), opcionSeleccionada.Objeto);

                            await ProcesarMenuTecnico(opcionAperturaTecnico);

                            break;

                        case eCausas.AperturaAutomatica:
                            Modos modoAperturaAutomatica = JsonConvert.DeserializeObject<Modos>(opcionSeleccionada.Objeto);

                            await ProcesarAperturaAutomatica(modoAperturaAutomatica, eOrigenComando.Pantalla);

                            break;

                        case eCausas.OpcionesSupervisor:
                            eAperturasSupervisor opcionAperturaSupervisor;

                            opcionAperturaSupervisor = (eAperturasSupervisor)Enum.Parse(typeof(eAperturasSupervisor), opcionSeleccionada.Objeto);

                            await ProcesarMenuSupervisor(opcionAperturaSupervisor);

                            break;

                        case eCausas.Retiro:
                            eOpcionesTeclaRetiro opcionTeclaRetiro;

                            opcionTeclaRetiro = (eOpcionesTeclaRetiro)Enum.Parse(typeof(eOpcionesTeclaRetiro), opcionSeleccionada.Objeto);

                            ProcesarMenuTeclaRetiro(opcionTeclaRetiro);

                            break;

                        case eCausas.VentaRecarga:
                            enmOpcionesRecarga opcionTeclaRecarga;

                            opcionTeclaRecarga = (enmOpcionesRecarga)Enum.Parse(typeof(enmOpcionesRecarga), opcionSeleccionada.Objeto);

                            if (opcionTeclaRecarga == enmOpcionesRecarga.Recarga)
                                TeclaRecarga(null);
                            //else if( opcionTeclaRecarga == enmOpcionesRecarga.CobroAbono )
                            //    TeclaCobroAbono( null );
                            else if( opcionTeclaRecarga == enmOpcionesRecarga.CobroDeuda )
                                RecibirCobroDeuda( null );

                            break;

                        case eCausas.MonedaRetiro:

                            SalvarMoneda(comandoJson);

                            break;
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Abortada)
                {
                    if (causaRecibida.Codigo == eCausas.SimulacionPaso || causaRecibida.Codigo == eCausas.CausaSimulacion)
                        _bSIPforzado = false;
                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);
            }
        }

        public override bool ModoPermite(ePermisosModos permisoModo)
        {
            bool permite = false;

            try
            {
                if (ModuloBaseDatos.Instance.PermisoModo.ContainsKey(permisoModo))
                {
                    permite = ModuloBaseDatos.Instance.PermisoModo[permisoModo];
                }
                else
                {
                    _logger?.Error("El diccionario de permisos modos no contiene la siguiente entrada:" + permisoModo.ToString());
                    permite = false;
                }
            }
            catch (Exception ex)
            {
                _loggerExcepciones?.Error(ex);

                permite = false;
            }

            return permite;
        }

        private void CargarOpcionMenu(ref ListadoOpciones opciones, string opcionMenu, bool confirmar, string descripcion, string keyDiccionarioTeclado, int orden, bool traducir = true)
        {
            if (keyDiccionarioTeclado == string.Empty || !_dictionarioTeclas.ContainsKey(keyDiccionarioTeclado) || keyDiccionarioTeclado == "PagoDiferido" || keyDiccionarioTeclado == "Vuelto")
            {
                if (opciones == null)
                    opciones = new ListadoOpciones();

                Opcion opcion = new Opcion();
                opcion.Objeto = opcionMenu;
                opcion.Confirmar = confirmar;
                opcion.Descripcion = (traducir == true) ? Traduccion.Traducir(descripcion) : descripcion;
                opcion.Orden = orden;

                opciones.ListaOpciones.Add(opcion);
            }
        }

        private TarifaABuscar GenerarTarifaABuscar(short categoria, byte tipoTarifa)
        {
            TarifaABuscar tarifaABuscar = new TarifaABuscar();

            if (tarifaABuscar != null)
            {
                tarifaABuscar.Catego = categoria;
                tarifaABuscar.Estacion = ModuloBaseDatos.Instance.ConfigVia.CodigoEstacion.GetValueOrDefault();
                tarifaABuscar.TipoTarifa = tipoTarifa; // TODO VER si existe algun caso donde no sea 0
                tarifaABuscar.FechAct = Fecha;
                tarifaABuscar.FechaComparacion = Fecha;
                tarifaABuscar.FechVal = Fecha;
                tarifaABuscar.Sentido = ModuloBaseDatos.Instance.ConfigVia.Sentido;
            }

            return tarifaABuscar;
        }

        private async Task<bool> CategoriaFormaPagoValida(char tipoOperacion, char tipoFPago = '\0', short categoria = 0)
        {
            bool retValue = true;

            CategoFPagoABuscar categoFPagoABuscar = new CategoFPagoABuscar();
            categoFPagoABuscar.Categoria = categoria;
            categoFPagoABuscar.TipoFPago = tipoFPago;
            categoFPagoABuscar.TipoOperacion = tipoOperacion;

            List<CategoFPago> categoFPago = await ModuloBaseDatos.Instance.BuscarCategoFPagoAsync(categoFPagoABuscar);

            if (categoFPago != null && !categoFPago.Any())
                retValue = false;

            return retValue;
        }

        public override bool GetUltimoSentidoEsOpuesto()
        {
            return _bUltimoSentidoEsOpuesto;
        }

        public override int GetComitivaVehPendientes()
        {
            return _nComitivaVehPendientes;
        }

        public override eEstadoVia GetEstadoVia()
        {
            return _estado;
        }

        override public void SetTurno(TurnoBD oTurno, bool bUltTurno, bool bInit)
        {
            //TODO si el turno estaba abierto hacer lo correspondiente(limpiar cola de vehiculos)
            _logger.Info("Set Turno -> Inicio");
            if (bUltTurno)
            {
                Parte oParte = new Parte();
                Operador oOperador = new Operador();
                _turno.Sentido = oTurno.Sentido;
                _turno.NumeroTurno = (ulong)oTurno.NumeroTurno;
                _turno.Parte = oParte;
                _turno.Operador = oOperador;
                _logger.Info("Set Turno -> bUltTurno");
            }
            else
            {
                if (oTurno != null)
                {
                    Parte oParte = new Parte();
                    Operador oOperador = new Operador();
                    _turno.Operador = oOperador;
                    _turno.FechaApertura = DateTime.Parse(oTurno.FechaApertura);
                    _turno.FechaCierre = DateTime.Parse(oTurno.FechaCierre);
                    _turno.NumeroTarjeta = (ulong)oTurno.NumeroTarjeta;
                    oParte.NombreCajero = oTurno.NombreCajero;
                    oParte.IDCajero = oTurno.NumeroTarjeta.ToString();
                    _turno.Modo = ClassUtiles.GetDescription(oTurno.ModoApertura);
                    _turno.OrigenApertura = oTurno.OrigenApertura == null ? '\0' : char.Parse(oTurno.OrigenApertura);
                    _turno.Mantenimiento = oTurno.ModoMantenimiento == null ? '\0' : char.Parse(oTurno.ModoMantenimiento);
                    _turno.ModoQuiebre = oTurno.ModoQuiebre == null ? '\0' : char.Parse(oTurno.ModoQuiebre);
                    oParte.NumeroParte = oTurno.NumeroParte;
                    oParte.IDCajero = oTurno.NumeroTarjeta.ToString();
                    _turno.SinPassword = oTurno.SinPassword == null ? '\0' : char.Parse(oTurno.SinPassword);
                    _turno.Sentido = oTurno.Sentido;
                    _turno.NumeroImpresoraFiscal = oTurno.NumeroImpresoraFiscal;
                    _turno.PuntoVenta = oTurno.NumeroPuntoVenta == null ? 0 : long.Parse(oTurno.NumeroPuntoVenta);
                    _turno.Parte = oParte;
                    _turno.NumeroTurno = (ulong)oTurno.NumeroTurno;
                    if (oTurno.TurnoAbierto == "S")
                    {
                        oOperador.ID = _turno.NumeroTarjeta.ToString();
                        oOperador.NumeroTarjeta = (long)_turno.NumeroTarjeta;
                        oOperador.Nombre = _turno.Parte.NombreCajero;
                        _turno.Operador = oOperador;
                        _turno.EstadoTurno = enmEstadoTurno.Abierta;
                        SetOperadorActual(_turno.Operador);
                    }
                    else
                        _turno.EstadoTurno = enmEstadoTurno.Cerrada;

                    _logger.Info("Set Turno -> oTurno");
                }
            }

            _turno.EstadoNumeracion = _estadoNumeracion;
            _turno.CodigoTurno = "";

            if (bInit)
            {
                _turno.NumeroVia = ModuloBaseDatos.Instance.ConfigVia.NumeroDeVia;
                _turno.NumeroEstacion = ModuloBaseDatos.Instance.ConfigVia.NumeroDeEstacion;

                if (string.IsNullOrEmpty(ModuloBaseDatos.Instance.ConfigVia.NumeroPuntoVta))
                    _turno.PuntoVenta = 0;
                else
                {
                    long lPunto;
                    string sPunto = ModuloBaseDatos.Instance.ConfigVia.NumeroPuntoVta.Trim();
                    _turno.PuntoVenta = long.TryParse(sPunto, out lPunto) ? lPunto : 0;
                }

                _timerFecha.Start();

                if (_turno.EstadoTurno == enmEstadoTurno.Abierta)
                    ChequearUltimoEstado();

                _timerControlCambioTarifa.Start();
                _timerChequeo.Start();

                ModuloBaseDatos.Instance.ProcesarActualizacion += ActualizaListasLocales;
            }

            // Update Online
            ModuloEventos.Instance.ActualizarTurno(_turno);
            UpdateOnline();

            _timerConsultaPesos.Start();
            // listaVideoAcc
            ModuloBaseDatos.Instance.BuscarVideoAcc(-1);
            _logger.Debug("SetTurno -> Obtengo informacion del turno al inicio [{0}]", bInit);
        }

        private void AsignarModoATurno()
        {
            if (_modo.Modo == ClassUtiles.GetEnumDescr(enmModoAperturaVia.Dinamico))
                _turno.ModoAperturaVia = enmModoAperturaVia.Dinamico;
            else if (_modo.Modo == ClassUtiles.GetEnumDescr(enmModoAperturaVia.MD))
                _turno.ModoAperturaVia = enmModoAperturaVia.MD;
            else if (_modo.Modo == ClassUtiles.GetEnumDescr(enmModoAperturaVia.Manual))
                _turno.ModoAperturaVia = enmModoAperturaVia.Manual;
            else if (_modo.Modo == ClassUtiles.GetEnumDescr(enmModoAperturaVia.AVI))
                _turno.ModoAperturaVia = enmModoAperturaVia.AVI;

            _turno.Modo = _modo.Modo;
        }

        #endregion

        #region Listas Locales para Cambio de Jornada, Runner y Cruce Horarios

        private HoraCierre _horaCierre;
        private Runner _runnerActual;
        private Tarifa _tarifaCat2Tipo0;

        private async void ActualizaListasLocales(eTablaBD eListaActualizada)
        {
            if (eListaActualizada == eTablaBD.C)
            {
                _horaCierre = ModuloBaseDatos.Instance.BuscarListaHoraCierre().Find(x => x.NroTurnoTesoreria == 1);
                _tarifaCat2Tipo0 = ModuloBaseDatos.Instance.BuscarTarifa(GenerarTarifaABuscar(2, 0));

                // Actualiza y envia expresiones regulares a pantalla
                ModuloPantalla.Instance.EnviarExpresionesRegulares(true);

                // Actualiza y envia los simbolos a pantalla
                ModuloPantalla.Instance.EnviarListaSimbolos(true);

                //Consulto nuevamente los pesos por si hubo algún cambio
                _timerConsultaPesos.Start();
            }
            else if (eListaActualizada == eTablaBD.R)
            {
                _runnerActual = await ModuloBaseDatos.Instance.BuscarRunnerActualAsync();
            }
        }
        #endregion

        #region Metodos relativos al Cambio de Jornada
        private object _lockCambioJornada = new object();

        /// <summary>
        /// Consulta si se esta en un cambio de jornada. Retorna la hora
        /// de cierre maxima.
        /// </summary>
        /// <param name="HoraCierreMax"></param>
        /// <param name="MinutosAntes"></param>
        /// <returns></returns>
        private bool IsCambioJornada(out DateTime HoraCierreMax, int MinutosAntes)
        {
            HoraCierreMax = DateTime.MinValue;

            if (!ModoPermite(ePermisosModos.CambioJornada))
                return false;

            _logger?.Info("IsCambioJornada -> Entro");

            int m_Hora, m_Min, m_ToleranciaAnt, m_ToleranciaPost;
            bool ret = false;

            lock (_lockCambioJornada)
            {
                List<HoraCierre> listaHorasCierre = new List<HoraCierre>();

                if (_horaCierre == null)
                    listaHorasCierre = ModuloBaseDatos.Instance.BuscarListaHoraCierre();

                if (!listaHorasCierre.Any())
                    return ret;

                HoraCierre dtAux = listaHorasCierre.Find(x => x.NroTurnoTesoreria == 1);

                if (dtAux != null)
                    _horaCierre = dtAux;

                m_Hora = _horaCierre.HoraInicialTurno.Hours;
                m_Min = _horaCierre.HoraInicialTurno.Minutes;
                m_ToleranciaAnt = _horaCierre.MinToleranciaAnterior;
                m_ToleranciaPost = _horaCierre.MinToleranciaPosterior;

                DateTime UltimaApertura = _turno.FechaApertura;
                DateTime HoraCierreAnterior = new DateTime(UltimaApertura.Year, UltimaApertura.Month, UltimaApertura.Day, m_Hora, m_Min, 0);
                if (m_ToleranciaAnt != 0)
                    HoraCierreAnterior = HoraCierreAnterior.AddMinutes(-m_ToleranciaAnt);

                DateTime HoraCierrePosterior = new DateTime();
                HoraCierrePosterior = HoraCierreAnterior.AddMinutes(m_ToleranciaPost);
                HoraCierreMax = HoraCierrePosterior;

                //Con Tolerancia Posterior
                if (ModoPermite(ePermisosModos.ToleranciaPosterior) && !ModoPermite(ePermisosModos.ToleranciaAnterior))
                {
                    if ((HoraCierrePosterior > UltimaApertura) && (HoraCierrePosterior <= Fecha))
                    {
                        _logger?.Info("IsCambioJornada -> Cambio de Jornada con tolerancia posterior");
                        ret = true;
                    }
                }

                //Con ambas tolerancias
                if (ModoPermite(ePermisosModos.ToleranciaPosterior) && ModoPermite(ePermisosModos.ToleranciaAnterior))
                {
                    if ((HoraCierreAnterior > UltimaApertura) && ((HoraCierrePosterior.AddMinutes(-MinutosAntes)) <= Fecha))
                    {
                        _logger?.Info("IsCambioJornada -> Cambio de Jornada con ambas tolerancias");
                        ret = true;
                    }
                }
            }
            _logger?.Info("IsCambioJornada -> Salgo");
            return ret;
        }

        public DateTime FechaCierreMax { set; get; }

        private bool EsCambioJornada()
        {
            bool nRet = false;
            DateTime dt = new DateTime();
            nRet = IsCambioJornada(out dt, 0);
            FechaCierreMax = dt;
            return nRet;
        }

        private bool SinEspacioEnDisco()
        {
            bool nRet = false;
            ulong BytesLibres = 0;
            if (ClassUtiles.DriveFreeBytes(Path.GetPathRoot(Environment.SystemDirectory), out BytesLibres))
            {
                //Compruebo cuanto espacio queda disponible
                double GigaBytesLibres = ClassUtiles.ConvertByteSize(BytesLibres, "MB");
                if (GigaBytesLibres <= _minimoEspacioLibreDiscoGB && (Estado == eEstadoVia.EVAbiertaLibre || Estado == eEstadoVia.EVCerrada))
                {
                    //No queda espacio en disco!
                    if (Estado == eEstadoVia.EVAbiertaLibre)
                    {
                        //Cierro el turno y pongo mensaje en pantalla
                        CierreTurno(false, eCodigoCierre.FinalTurno, eOrigenComando.Pantalla);
                    }
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("VÍA SIN ESPACIO EN DISCO"));
                    //Todo: enviar la alarma o el evento correspondiente
                    nRet = true;
                }
            }
            return nRet;
        }

        private async void TimerRevisaCambioJornada()
        {
            if (ModoPermite(ePermisosModos.CambioJornada))
            {
                DateTime dt = new DateTime();

                if (_estado != eEstadoVia.EVCerrada)
                {
                    if (IsCambioJornada(out dt, 0)) //Es el cambio de jornada?
                    {
                        if (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat)
                        {
                            _logger?.Info("TimerRevisaCambioJornada -> Cierre de turno por Cambio de Jornada");

                            bool hacerReapertura = ModoPermite(ePermisosModos.ReaperturaCambioJornada);

                            //Cierro el turno o bloqueo la operatoria
                            await CierreTurno(hacerReapertura, eCodigoCierre.CambioDeJornada, eOrigenComando.Pantalla);

                            if (hacerReapertura)
                            {
                                _logger?.Info("TimerRevisaCambioJornada -> Apertura de turno por Cambio de Jornada");
                                if (_turno.OrigenApertura == 'M')
                                    await AperturaTurno(_modo, _operadorActual, true, eOrigenComando.Pantalla);
                                else if (_turno.OrigenApertura == 'R')
                                    await AperturaTurno(_modo, _operadorActual, true, eOrigenComando.Automatica);
                                else
                                    await AperturaTurno(_modo, _operadorActual, true, eOrigenComando.Supervision);
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                            }
                            else
                            {
                                _logger?.Info("TimerRevisaCambioJornada -> Via no permite reapertura automatica");
                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ReabrirPorCambioJornada);
                            }
                        }
                        else
                        {
                            // Incremento alarma de via en estado esperando cambio a medianoche
                            ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.EsperandoCambioJornada, 0);
                        }
                    }
                    //Estoy cercano a un cambio de jornada?
                    else if (_horaCierre != null && _horaCierre.RegEstaEnHoraCambioJornada(_turno.FechaApertura)) //Estoy cercano a un cambio de jornada?
                    {
                        if (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat)
                        {
                            // Muestra el mensaje de cambio de jornada cada 10 segundos
                            if (!_timerCambioJornada.Enabled)
                                _timerCambioJornada.Start();
                            //Mensaje en pantalla (alarma?)
                            //ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornadaProximo);
                        }
                    }
                    else
                    {
                        ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.EsperandoCambioJornada, 0);
                        if (_horaCierre == null)
                            _horaCierre = ModuloBaseDatos.Instance.BuscarListaHoraCierre().Find(x => x.NroTurnoTesoreria == 1);
                    }
                }
            }
        }

        #endregion

        #region Metodos Relativos al Cambio de Runner

        public bool CambioRunner { set; get; }

        private async void TimerRevisaCambioRunner()
        {
            if (_turno.Mantenimiento != 'S') //si no estoy en modo mantenimiento
            {
                bool bCambioRunner = false;
                DateTime HoraCierreMax = new DateTime();

                if ((_modo?.Cajero == "N") && (_estado == eEstadoVia.EVAbiertaLibre || _estado == eEstadoVia.EVAbiertaCat))
                {
                    _runnerActual = ModuloBaseDatos.Instance.BuscarRunnerActual();

                    if (_runnerActual.IDSupervisor != null && (_turno.Operador.ID != _runnerActual.IDSupervisor || _turno.FechaApertura < _runnerActual.FechaInicioTurno))
                    {
                        _logger?.Info("TimerRevisaCambioRunner -> Cambio de Runner -> Actual [{0}] - Nuevo [{1}] - FechaInicioTurno [{2}]", _turno.Operador?.ID, _runnerActual?.IDSupervisor, _runnerActual?.FechaInicioTurno.ToString());
                        //Cierro turno y abro uno nuevo a cargo del supervisor
                        await CierreTurno(true, eCodigoCierre.CambioDeResponsable, eOrigenComando.Pantalla);

                        Operador opeAux = ModuloBaseDatos.Instance.BuscarOperadorPrivate(_runnerActual.IDSupervisor);

                        Operador nuevoOperador = new Operador();
                        nuevoOperador.ID = opeAux.ID;
                        nuevoOperador.Nombre = opeAux.Nombre;

                        if (_turno.OrigenApertura == 'M')
                            await AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Pantalla);
                        else if (_turno.OrigenApertura == 'R')
                            await AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Automatica);
                        else
                            await AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Supervision);

                        bCambioRunner = true;
                    }
                    else if (IsCierreTurno(out HoraCierreMax))
                    {
                        _logger?.Info("TimerRevisaCambioRunner -> Cierre de Turno y Cambio de Runner");
                        //Cierre de Turno Via Dinámica
                        FechaCierreMax = HoraCierreMax;
                        _turno.Operador.ID = _runnerActual.IDSupervisor;
                        bCambioRunner = true;
                    }
                }
                if (CambioRunner != bCambioRunner)
                {
                    CambioRunner = bCambioRunner;
                    _logger?.Info("TimerRevisaCambioRunner -> Cambio de Runner modo con cajero");
                }
            }
            else
            {
                if (CambioRunner)//modo mantenimiento
                {
                    CambioRunner = false;
                }
            }
        }

        private bool IsCierreTurno(out DateTime HoraCierreMax)
        {
            bool ret = false;
            DateTime UltimaApertura = _turno.FechaCierre;

            int m_Hora, m_Min, m_ToleranciaPost;
            HoraCierreMax = DateTime.MinValue;

            List<HoraCierre> listHoraCierreAux = new List<HoraCierre>();

            if (_horaCierre == null)
                listHoraCierreAux = ModuloBaseDatos.Instance.BuscarListaHoraCierre();

            if (!listHoraCierreAux.Any())
                return ret;

            HoraCierre HoraCierreAux;
            HoraCierreAux = listHoraCierreAux.Find(x => x.NroTurnoTesoreria == 1);

            if (HoraCierreAux != null)
                _horaCierre = HoraCierreAux;

            m_Hora = _horaCierre.HoraInicialTurno.Hours;
            m_Min = _horaCierre.HoraInicialTurno.Minutes;
            m_ToleranciaPost = _horaCierre.MinToleranciaPosterior;

            //Para Cierre de Turno de Vias Dinámicas
            DateTime HoraCierreHoy = new DateTime(Fecha.Year, Fecha.Month, Fecha.Day, m_Hora, m_Min, 0);
            if (m_ToleranciaPost != 0)
                HoraCierreHoy = HoraCierreHoy.AddMinutes(m_ToleranciaPost);

            //Sin tener en cuenta las tolerancias
            if (HoraCierreHoy > UltimaApertura && HoraCierreHoy < Fecha)
                ret = true;

            HoraCierreMax = HoraCierreHoy;

            return ret;
        }

        #endregion

        #region Revisa Cambio de Tarifa

        private string _ultimoTipodh = "";

        private bool AbriendoTurno { set; get; }
        private bool CerrandoTurno { set; get; }
        private bool CambioBloque { set; get; }
        private bool FinalJornada { set; get; }
        private DateTime UltimaFechaCobrada { set; get; }

        private void TimerChequeoCambioTarifa(object source, ElapsedEventArgs e)
        {
            if (_tarifaCat2Tipo0 != null)
            {
                RevisaCambioTarifa(_tarifaCat2Tipo0.Fecha, _tarifaCat2Tipo0.CodigoHorario, true);
                _logger?.Trace("TimerChequeoCambioTarifa -> RevisaCambioTarifa");
            }
            else
            {
                _tarifaCat2Tipo0 = ModuloBaseDatos.Instance.BuscarTarifa(GenerarTarifaABuscar(2, 0));
            }
        }

        private void TimerCambioTarifaCierra(object source, ElapsedEventArgs e)
        {
            lock (_lockRevisaCambioTarifaTimer)
            {
                // Si ya esta vacia y no estamos cerrando el turno
                if (_logicaVia.ViaSinVehPagados() && !CerrandoTurno)
                {
                    //Si no esta cerrada
                    if (_estado != eEstadoVia.EVCerrada)
                    {
                        Operador oOpeAux;
                        //Verifica Cambio de Runner Pendientes cada medio segundo
                        if (CambioRunner)
                        {
                            if (_init)
                            {
                                _logger?.Info("TimerCambioTarifaCierra -> Cambio de Runner");
                                //Cambio de Bloque con nuevo Runner 
                                AbriendoTurno = true;
                                _turno.CausaCierre = eCodigoCierre.CambioDeResponsable;

                                CierreTurno(true, eCodigoCierre.CambioDeResponsable, eOrigenComando.Pantalla);
                                oOpeAux = ModuloBaseDatos.Instance.BuscarOperadorPrivate(_runnerActual.IDSupervisor);

                                bool hacerReapertura = ModoPermite(ePermisosModos.ReaperturaCambioTarifa);

                                if (hacerReapertura)
                                {
                                    _logger?.Info("RevisaCambioTarifa -> Apertura de turno por Cambio de Tarifa");

                                    Operador nuevoOperador = new Operador();
                                    _runnerActual = ModuloBaseDatos.Instance.BuscarRunnerActual();

                                    nuevoOperador.ID = _runnerActual.IDSupervisor;
                                    nuevoOperador.Nombre = _runnerActual.IDSupervisor;
                                    _turno.Operador.ID = _runnerActual.IDSupervisor;

                                    if (_turno.OrigenApertura == 'M')
                                        AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Pantalla);
                                    else if (_turno.OrigenApertura == 'R')
                                        AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Automatica);
                                    else
                                        AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Supervision);

                                    AbriendoTurno = false;
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                                }
                                else
                                {
                                    _logger?.Info("RevisaCambioTarifa -> Via no permite reapertura automatica");
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ReabrirPorCambioTarifa);
                                }

                            }
                        }
                        //Hacemos uno u otro
                        //Verifica cambio de Bloque
                        else if (CambioBloque)
                        {
                            if (_init)
                            {
                                _logger?.Info("TimerCambioTarifaCierra -> Cambio de Bloque");
                                AbriendoTurno = true;
                                //Buscamos de nuevo por las dudas porque 
                                //los intentos de apertura lo pueden haber pisado
                                oOpeAux = ModuloBaseDatos.Instance.BuscarOperadorPrivate(_turno.Operador.ID);
                                if (FinalJornada)
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.FinalJornada);
                                else
                                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.CambioBloqueRealizado);
                                FinalJornada = false; //Paro de mostrar el mensaje intermitente
                                AbriendoTurno = false;
                            }
                        }
                    }
                    //Si esta cerrada no preciso mas el cambio de runner o bloque
                    CambioRunner = false;
                    CambioBloque = false;
                }
                _timerCambioTarifa.Enabled = false;
            }
        }

        private bool EsCambioTarifa(bool ejecutaCierre)
        {
            bool nRet = false;

            Tarifa tarifa = ModuloBaseDatos.Instance.BuscarTarifa(GenerarTarifaABuscar(2, 0));

            if (tarifa != null && tarifa.Fecha != DateTime.MinValue)
                nRet = RevisaCambioTarifa(tarifa.Fecha, tarifa.CodigoHorario, ejecutaCierre);
            return nRet;
        }

        private bool RevisaCambioTarifa(DateTime FechVal, string csTipoDia, bool bEjecutaCierre)
        {
            bool bCambioTurno = false;
            bool nRet = false;

            lock (_lockRevisaCambioTarifa)
            {
                //Si la tarifa es valida luego de haber abierto el turno
                // o Tipo de dias distintos, entonces hago cierre y apertura turno.

                if (FechVal > _turno.FechaApertura || (!string.IsNullOrEmpty(_ultimoTipodh) && csTipoDia != _ultimoTipodh))
                {
                    if (FechVal > DateTime.MinValue && _ultimoTipodh != "" && (_estado != eEstadoVia.EVCerrada && !GetUltimoSentidoEsOpuesto()))
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                        _logger?.Info("RevisaCambioTarifa -> Cambio de Tarifa");
                        nRet = true;
                    }

                    if (bEjecutaCierre && !AbriendoTurno)
                    {
                        //Si estamos en Modo D y la via no está vacía
                        //o tiene vehiculos en cola con pago adelantado
                        //o no esta inicializada
                        if (_turno.ModoAperturaVia == enmModoAperturaVia.Dinamico && !_logicaVia.ViaSinVehPagados() || !_init)
                        {
                            if (FechVal > DateTime.MinValue && _ultimoTipodh != "")
                            {
                                //Generamos un Timer para que cambie de bloque
                                if (FechVal > _turno.FechaApertura)
                                {
                                    FechaCierreMax = FechVal;
                                }
                                else
                                {
                                    FechaCierreMax = Fecha;
                                }

                                CambioBloque = true;
                                _logger?.Info("RevisaCambioTarifa -> Cambio de Bloque");
                                //_timerCambioTarifa.Start();     //Revisar FAB
                            }
                        }
                        else
                        {
                            if (FechVal > DateTime.MinValue)
                            {
                                AbriendoTurno = true;
                                bCambioTurno = true;
                                _logger?.Info("RevisaCambioTarifa -> Cambio de Turno");
                                _turno.CausaCierre = eCodigoCierre.CambioDeTarifa;
                            }
                        }
                    }

                    //Generar un evento de cambio de tarifa con el valor de la tarifa 
                    //de esta estación, categoría 2, tipo de tarifa 0
                    if (UltimaFechaCobrada != FechVal)
                    {
                        if (_tarifaCat2Tipo0 == null)  //No encontro la tarifa
                            _tarifaCat2Tipo0 = ModuloBaseDatos.Instance.BuscarTarifa(GenerarTarifaABuscar(2, 0));

                        EventoCambioTarifas EvCambioTarifa = new EventoCambioTarifas();
                        EvCambioTarifa.Tarifa = _tarifaCat2Tipo0.Valor;
                        EvCambioTarifa.Tipdh = csTipoDia;
                        ModuloEventos.Instance.SetCambioTarifas(_turno, EvCambioTarifa);
                    }

                    // Se realiza un cambio de turno siempre que la via no este  o abierta en sentido opuesto
                    if (bCambioTurno && (_estado != eEstadoVia.EVCerrada && !GetUltimoSentidoEsOpuesto()))
                    {
                        bool hacerReapertura = ModoPermite(ePermisosModos.ReaperturaCambioTarifa);
                        Operador nuevoOperador = new Operador();
                        nuevoOperador = _operadorActual;

                        CierreTurno(hacerReapertura, eCodigoCierre.CambioDeTarifa, eOrigenComando.Pantalla);
                        _logger?.Info("RevisaCambioTarifa -> Cierre y Cambio de Turno");

                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);

                        if (hacerReapertura)
                        {
                            _logger?.Info("RevisaCambioTarifa -> Apertura de turno por Cambio de Tarifa");

                            /*_runnerActual = ModuloBaseDatos.Instance.BuscarRunnerActual();
                            Operador nuevoOperador = new Operador();
                            nuevoOperador.ID = _runnerActual.IDSupervisor;
                            nuevoOperador.Nombre = _runnerActual.IDSupervisor;
                            _turno.Operador.ID = _runnerActual.IDSupervisor;*/

                            _turno.Operador.ID = nuevoOperador.ID;

                            if (_turno.OrigenApertura == 'M')
                                AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Pantalla);
                            else if (_turno.OrigenApertura == 'R')
                                AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Automatica);
                            else
                                AperturaTurno(_modo, nuevoOperador, true, eOrigenComando.Supervision);

                            AbriendoTurno = false;
                        }
                        else
                        {
                            _logger?.Info("RevisaCambioTarifa -> Via no permite reapertura automatica");
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ReabrirPorCambioTarifa);
                        }
                    }
                }
                _ultimoTipodh = string.IsNullOrEmpty(csTipoDia) ? _ultimoTipodh : csTipoDia;
                UltimaFechaCobrada = FechVal;
                AbriendoTurno = false;
            }
            return nRet;
        }


        #endregion

        #region Estado de Via sin Transito
        private object _lockEveSinTransito = new object();
        /// <summary>
        /// Envía un evento si la via pasa x minutos sin tránsito
        /// </summary>
        private void EstadoViaSinTransito()
        {
            int MinViaSinTran = ModuloBaseDatos.Instance.ConfigVia.TiempoViaSinTransito;
            if (MinViaSinTran == 0) MinViaSinTran = 2;

            lock (_lockEveSinTransito)
            {
                if (Fecha >= FecUltimoTransito.AddMinutes(MinViaSinTran) && FecUltimoTransito != DateTime.MinValue)
                {
                    FecUltimoTransito = Fecha;
                    FallaCritica falla = new FallaCritica();
                    falla.CodFallaCritica = EnmFallaCritica.FCViaSTran;
                    falla.Observacion = _estado == eEstadoVia.EVCerrada ? "Via Cerrada" : "Via Abierta";
                    ModuloEventos.Instance.SetFallasCriticas(_turno, falla, null);
                    if (_estado == eEstadoVia.EVCerrada)
                    {
                        Turno turno = new Turno();
                        turno.FechaCierre = Fecha;
                        Operador oper = new Operador();
                        oper.ID = falla.Observacion;
                        turno.Operador = oper;
                        ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.CierreVia, ModuloBaseDatos.Instance.ConfigVia, turno, null);
                    }
                }
            }
        }

        public override void SetEstadoNumeracion(eEstadoNumeracion estadoNumeracion, Vars numeracion, bool bInit = true)
        {
            _estadoNumeracion = estadoNumeracion;
            _logger.Info("Set Numeracion -> Inicio");
            if (numeracion != null)
                _numeracion = numeracion;

            if (_modo == null)
            {
                var task = Task.Run(async () => (await ModuloBaseDatos.Instance.BuscarModosAsync(ModuloBaseDatos.Instance.ConfigVia.ModeloVia)));
                List<Modos> modos = task.Result;

                if (modos != null && modos.Any())
                {
                    _modo = modos[0];

                    //Obtengo lista de PermisosModo
                    ModuloBaseDatos.Instance.BuscarListaPermisos(_modo.Modo);
                }
            }

            //Si el estado de la numeracion es por Confirmar pero el modo no permite la autorizacion, lo paso a NumeracionOK
            if (_estadoNumeracion == eEstadoNumeracion.NumeracionSinConfirmar && !ModoPermite(ePermisosModos.AutorizarNumeracion))
            {
                _estadoNumeracion = eEstadoNumeracion.NumeracionOk;
                InicializarPantalla(false);
                ModuloBaseDatos.Instance.ActualizarEstadoNumeracion(_estadoNumeracion);
            }

            if (_estadoNumeracion == eEstadoNumeracion.NumeracionOk)
            {
                if (!_seConfirmoNum)
                    ModuloPantalla.Instance.LimpiarMensajes();

                if (!numeracion.ListadoVehiculos.All(x => x == null))
                    _logicaVia.SetVehiculo(numeracion.ListadoVehiculos);

                if (numeracion.InformacionTurno != null)
                {
                    SetTurno(numeracion.InformacionTurno, numeracion.UltimoTurno, bInit);
                    _logger.Info("Se actualizo el turno");
                }                    

                ModuloAlarmas.Instance.LimpiarAlarma(eModuloAlarmas.NumeracionSinConfirmar, 1);
            }
            else
            {
                if (_estadoNumeracion == eEstadoNumeracion.NumeracionSinConfirmar)
                    ModuloAlarmas.Instance.IncrementarAlarma(eModuloAlarmas.NumeracionSinConfirmar, 1);

                SetTurno(numeracion.InformacionTurno, numeracion.UltimoTurno, bInit);
                _logger.Info("Se actualizo el turno");
            }
        }
        #endregion

        #region Reimprimir Ticket
        public async void ProcesarReimprimirTicket(eOpcionMenu comando)
        {
            await ProcesarReimprimirTicket(eTipoComando.eTecla);
        }

        private async Task ProcesarReimprimirTicket(eTipoComando tipoComando)
        {
            Vehiculo vehiculo = _logicaVia.GetPrimeroSegundoVehiculo();

            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                ImprimiendoTicket = true;

                if (vehiculo.TipBo == 'F' || (vehiculo.InfoCliente.Ruc.StartsWith("20") && vehiculo.InfoCliente.Ruc.Length == 11))
                {
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.FacturaReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                    {
                        ImprimiendoTicket = true;
                        errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.FacturaReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                        ImprimiendoTicket = false;
                    }
                }
                else
                {
                    if (_nComitivaVehPendientes > 0)
                    {
                        await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.ComitivaEfectivoReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                        {
                            ImprimiendoTicket = true;

                            errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.ComitivaEfectivoReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                            ImprimiendoTicket = false;
                        }
                    }
                    else
                    {
                        await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.EfectivoReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                        {
                            ImprimiendoTicket = true;
                            errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EfectivoReimpresion, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                            ImprimiendoTicket = false;
                        }
                    }
                }

                ImprimiendoTicket = false;
            }

            ModuloEventos.Instance.SetEventoReimpresion(_turno, vehiculo, ModuloBaseDatos.Instance.ConfigVia);
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
        }

        #endregion

        #region Tecla Detraccion
        public async void TeclaDetraccion(eOpcionMenu comando)
        {
            await ProcesarDetraccion(eTipoComando.eTecla, null);
        }

        private async Task ProcesarDetraccion(eTipoComando tipoComando, MensajesDetraccion mensajeDetraccion)
        {
            if (tipoComando == eTipoComando.eTecla)
            {
                List<MensajesDetraccion> listaMensajesDetraccion = await ModuloBaseDatos.Instance.BuscarMensajesDetraccionAsync();

                ListadoOpciones opciones = new ListadoOpciones();

                Causa causa = new Causa();
                causa.Codigo = eCausas.MensajesDetraccion;
                causa.Descripcion = ClassUtiles.GetEnumDescr(eCausas.MensajesDetraccion);
                int orden = 1;

                if (listaMensajesDetraccion?.Count > 0)
                {
                    foreach (MensajesDetraccion causas in listaMensajesDetraccion)
                    {
                        CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(causas), true, causas.Texto, string.Empty, orden++, false);
                    }
                }
                else
                {
                    MensajesDetraccion mensaje = new MensajesDetraccion();
                    mensaje.Texto = "No paga por exento";
                    mensaje.Codigo = 1;
                    CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(mensaje), true, mensaje.Texto, string.Empty, orden++, false);
                    mensaje.Texto = "Conductor se niega a pagar";
                    mensaje.Codigo = 2;
                    CargarOpcionMenu(ref opciones, JsonConvert.SerializeObject(mensaje), true, mensaje.Texto, string.Empty, orden++, false);                    
                }
                opciones.MuestraOpcionIndividual = false;


                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(opciones, ref listaDatosVia);

                // Envio la lista de mensajes a supervision a pantalla
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MUESTRA_MENU, listaDatosVia);
            }
            else if (tipoComando == eTipoComando.eSeleccion)
            {
                await EnviarMensajeDetraccion(mensajeDetraccion);
            }

        }

        private async Task EnviarMensajeDetraccion(MensajesDetraccion mensajeDetraccion)
        {
            EnmStatusBD respuestaEvento = await ModuloEventos.Instance.SetMensajeAsync(_turno, (byte)mensajeDetraccion.Codigo);
            Vehiculo vehiculo;

            if (respuestaEvento != EnmStatusBD.OK)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error enviando mensaje de Detraccion"));
            else
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, mensajeDetraccion.Texto);
                vehiculo = _logicaVia.GetPrimerVehiculo();
                vehiculo.EstadoDetraccion = mensajeDetraccion.Codigo;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Tarifa Total" + ": " + ClassUtiles.FormatearMonedaAString(vehiculo.Tarifa));
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia);
            }
        }

        #endregion

        #region Cobro Deuda

        public bool ValidarPrecondicionesCobroDeuda()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else
            {
                if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0 || _modo.Cajero != "S")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado + "o");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.ModoConCajero);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.CobroManualSobreLazo) && _logicaVia.EstaOcupadoBucleSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (_logicaVia.GetVehIng().Patente == string.Empty)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);
                    retValue = false;
                }
                //else if( La fecha actual es la misma que la fecha en que se abrió el turno )
                //{
                //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa );
                //    retValue = false;
                //}
            }

            return retValue;
        }

        public async void RecibirCobroDeuda(ComandoLogica comando)
        {
            if (comando == null)
                await ProcesarCobroDeuda(eTipoComando.eTecla);
            else if (comando.Accion == enmAccion.COBRODEUDAS)
            {
                Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);

                await ProcesarCobroDeuda(eTipoComando.eConfirmacion, vehiculo);
            }
        }

        /// <summary>
        /// Procesa comando de pantalla para buscar si el vehiculo tiene deudas
        /// </summary>
        public async Task ProcesarCobroDeuda(eTipoComando tipoComando, Vehiculo vehiculo = null)
        {
            if (ValidarPrecondicionesCobroDeuda())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    await ProcesarPatente(eTipoComando.eValidacion, _logicaVia.GetVehIng(), eCausas.CobroDeuda, true);
                }
                if (tipoComando == eTipoComando.eValidacion)
                {
                    vehiculo = _logicaVia.GetVehIng();
                    List<InfoDeuda> listaDeudas = new List<InfoDeuda>();
                    List<Diferido> listaPagosDifPendientes = await ModuloBaseDatos.Instance.BuscarPagosDiferidosAsync(vehiculo.Patente);
                    List<Violacion> listaViolaciones = await ModuloBaseDatos.Instance.BuscarViolacionesAsync(vehiculo.Patente);
                    InfoDeuda deuda;

                    if (listaPagosDifPendientes.Count != 0)
                    {
                        foreach (Diferido item in listaPagosDifPendientes)
                        {
                            deuda = new InfoDeuda();
                            deuda.Tipo = eTipoDeuda.PagoDiferido;
                            deuda.Categoria = item.CategoriaGeneracion;
                            deuda.FechaHora = item.FechaGeneracion.GetValueOrDefault();
                            deuda.Estacion = (short)item.EstacionGeneracion;
                            deuda.Monto = item.ImporteGenerado.GetValueOrDefault();
                            deuda.DescripcionCategoria = item.DescripcionCategoria;
                            deuda.NombreEstacion = item.NombreEstacion;
                            deuda.Via = item.ViaGeneracion;
                            deuda.Id = item.NumeroPagoDiferido;
                            listaDeudas.Add(deuda);
                        }
                    }

                    if (listaViolaciones.Count != 0)
                    {
                        foreach (Violacion item in listaViolaciones)
                        {
                            deuda = new InfoDeuda();
                            deuda.Tipo = eTipoDeuda.Violacion;
                            deuda.Categoria = item.CategoriaGeneracion;
                            deuda.FechaHora = item.FechaGeneracion;
                            deuda.Estacion = (short)item.EstacionGeneracion;
                            deuda.Monto = item.ImporteGenerado;
                            deuda.DescripcionCategoria = item.DescripcionCategoria;
                            deuda.NombreEstacion = item.NombreEstacion;
                            deuda.Via = item.ViaGeneracion;
                            deuda.Id = 0;
                            listaDeudas.Add(deuda);
                        }
                    }

                    if (listaDeudas.Count != 0)
                    {
                        vehiculo.ListaInfoDeuda = listaDeudas;
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaDeudas, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.COBRODEUDAS, listaDatosVia);
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoPoseeDeudas);
                        List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
                    }
                }
                else if (tipoComando == eTipoComando.eConfirmacion)
                {
                    await ConfirmarCobroDeuda(vehiculo);
                }
            }
            else
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
        }

        /// <summary>
        /// Recibe de pantalla las deudas que se cobraron y graba en la base de datos que fueron pagadas
        /// </summary>
        private async Task ConfirmarCobroDeuda(Vehiculo vehiculoDeuda)
        {
            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            vehiculoDeuda.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                ImprimiendoTicket = true;

                errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.DeudasFactura, vehiculoDeuda, _turno, ModuloBaseDatos.Instance.ConfigVia);

                ImprimiendoTicket = false;
            }

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                VentaBD oVenta = new VentaBD();
                int nTotalDifer = 0, nTotalVio = 0;
                decimal dMontoDifer = 0, dMontoVio = 0;

                // Display msg DEUDA COBRADA ( configurar en mensajes display)
                ModuloDisplay.Instance.Enviar(eDisplay.VAR, vehiculo, "DEUDA\nPAGADA");

                // Sumar pago en total de ventas de turnos TODO VER
                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);
                ModuloEventos.Instance.ActualizarTurno(_turno);

                if (vehiculo.EstaPagado)
                {
                    _estado = eEstadoVia.EVAbiertaPag;
                    vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;

                    // Se actualizan perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                    _logger?.Debug("ConfirmarCobroDeuda -> BARRERA ARRIBA!!");

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    // Actualiza el mensaje en pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoEfectivo);
                }
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.DeudasPagadas);

                // Enviar el evento de ventas cuando se genere el tránsito del vehículo
                //OperacionVenta venta;
                DateTime FechaProcesada = DateTime.Now;
                foreach (var item in vehiculoDeuda.ListaInfoDeuda)
                {
                    //Modificar registro en la BD local
                    if (item.Tipo == eTipoDeuda.PagoDiferido)
                    {
                        await ModuloBaseDatos.Instance.ModificarPagoDiferidoBDAsync(item.Id, item.FechaHora);
                        nTotalDifer++;
                        dMontoDifer += item.Monto;
                    }
                    else if (item.Tipo == eTipoDeuda.Violacion)
                    {
                        ModuloEventos.Instance.SetCobroViolaciones(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo, item);
                        await ModuloBaseDatos.Instance.ModificarViolacionesBDAsync(vehiculoDeuda.Patente, item.FechaHora);
                        nTotalVio++;
                        dMontoVio += item.Monto;
                    }
                }

                //Se suma el tipo de venta en la BD local
                oVenta.CategoriaManual = vehiculo.Categoria;

                //Se suma el total de Pagos diferidos
                if (nTotalDifer > 0)
                {
                    oVenta.TipoVenta = eVentas.D;
                    oVenta.Monto = dMontoDifer;
                    oVenta.CantidadManual = nTotalDifer;
                    await ModuloBaseDatos.Instance.AlmacenarVentaTurnoAsync(oVenta);
                    await ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.CbrPagoDifer);
                }

                //Se suma el total de Violaciones
                if (nTotalVio > 0)
                {
                    oVenta.TipoVenta = eVentas.V;
                    oVenta.Monto = dMontoVio;
                    oVenta.CantidadManual = nTotalVio;
                    await ModuloBaseDatos.Instance.AlmacenarVentaTurnoAsync(oVenta);
                    await ModuloBaseDatos.Instance.AlmacenarAnomaliaTurnoAsync(eAnomalias.CbrViolac);
                }
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, listaDatosVia2);
            }
            else
            {
                // Envia el error de impresora a pantalla
                ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
            }
        }

        #endregion

        #region TarjetaChip

        private void ProcesarTarjetaChip(eEstadoAntena estado, RespuestaChip respuesta, eTipoLecturaTag tipoLectura)
        {
            Tag oTag = new Tag();
            oTag.NumeroTag = respuesta.LecturaTarjeta.Dispositivo;
            oTag.HoraLectura = DateTime.Now;
            //_logicaVia.GetVehIng().CobroEnCurso = true;
            _logicaVia.ProcesarLecturaTag(estado, oTag, tipoLectura);
        }

        private async Task PagadoTarjetaChip()
        {
            try
            {
                _logger.Info("PagadoTarjetaChip -> Inicio");

                InfoCliente cliente = new InfoCliente();
                ClienteDB clienteDB = new ClienteDB();

                Vehiculo vehiculo = _logicaVia.GetVehIng();
                ulong ulNroVehiculo = vehiculo.NumeroVehiculo;

                //algo pasó con la categoria, utilizamos la tabulada al validar en BD
                if (vehiculo.Categoria == 0)
                    vehiculo.Categoria = vehiculo.InfoTag.CategoTabulada;

                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(vehiculo.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                vehiculo.NoPermiteTag = false;
                //vehiculo.CobroEnCurso = true;
                vehiculo.TipOp = vehiculo.InfoTag.TipOp;
                vehiculo.TipBo = vehiculo.InfoTag.TipBo;
                vehiculo.FormaPago = eFormaPago.CTChPrepago;//TODO: switch el tipbo
                vehiculo.Fecha = Fecha;
                vehiculo.FechaFiscal = Fecha;
                vehiculo.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    vehiculo.NumeroDetraccion = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroDetraccion);

                cliente.RazonSocial = vehiculo.InfoTag.NombreCuenta;
                cliente.Ruc = vehiculo.InfoTag.Ruc;
                cliente.Codigo = vehiculo.InfoTag.NumeroTag;

                vehiculo.InfoCliente = cliente;

                clienteDB.Nombre = cliente.RazonSocial;
                clienteDB.NumeroDocumento = cliente.Ruc;

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                //busca nuevamente el vehiculo por si se movio
                ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    vehiculo.NoPermiteTag = false;
                    vehiculo.CobroEnCurso = false;
                    //vehiculo.TipOp = ' ';
                    vehiculo.FormaPago = eFormaPago.Nada;
                    vehiculo.FechaFiscal = DateTime.MinValue;
                    vehiculo.ClaveAcceso = string.Empty;
                    vehiculo.NumeroTicketF = 0;
                    if (vehiculo.AfectaDetraccion == 'S' && vehiculo.EstadoDetraccion == 3)
                    {
                        vehiculo.NumeroDetraccion = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroDetraccion);
                    }
                    ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    _logger.Debug($"PagadoTarjetaChip-> Decrementa nro ticket. Actual [{ulNro}]");

                    ModuloPantalla.Instance.LimpiarMensajes();
                    // Envia el error de impresora a pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + ClassUtiles.GetEnumDescr(errorImpresora));
                }
                else
                {
                    _estado = eEstadoVia.EVAbiertaPag;
                    //Se incrementa tránsito
                    vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                    // Se actualizan perifericos
                    DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                    DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                    _logger?.Debug("PagadoTarjetaChip -> BARRERA ARRIBA!!");

                    // Actualiza el estado de los mimicos en pantalla
                    Mimicos mimicos = new Mimicos();
                    DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                    // Actualiza el mensaje en pantalla
                    string sMensajeP = string.IsNullOrEmpty(vehiculo.InfoTag?.Mensaje) ? vehiculo.InfoTag?.ErrorTag.ToString() : vehiculo.InfoTag?.Mensaje;
                    sMensajeP += " ( " + ClassUtiles.FormatearMonedaAString(vehiculo.InfoTag.SaldoFinal) + " )";

                    string sMensajeD = string.IsNullOrEmpty(vehiculo.InfoTag?.MensajeDisplay) ? vehiculo.InfoTag?.ErrorTag.ToString() : vehiculo.InfoTag?.MensajeDisplay;

                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, sMensajeP, false);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, Traduccion.Traducir("Patente") + $": {vehiculo.InfoTag.Patente}", false);
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Nro: {vehiculo.InfoTag.NumeroTag.Replace(" ", "")}", false);

                    // Actualiza el estado de vehiculo en pantalla
                    listaDatosVia = new List<DatoVia>();
                    ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(clienteDB, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                    //Capturo foto y video
                    _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);
                    //almacena la foto
                    ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                    // Envia mensaje a display
                    ModuloDisplay.Instance.Enviar(eDisplay.VAR, vehiculo, sMensajeD);

                    //busca nuevamente el vehiculo por si se movio
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    ModuloBaseDatos.Instance.AlmacenarPagadoTurno(vehiculo, _turno);

                    //busca nuevamente el vehiculo por si se movio
                    ConfirmarVehiculo(ulNroVehiculo, ref vehiculo);

                    //Se envía setCobro
                    vehiculo.Operacion = "CB";
                    ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehiculo);

                    ModuloVideoContinuo.Instance.EnviarMensaje(eMensajesVideoContinuo.PagadoTagChip, null, null, vehiculo, vehiculo.InfoTag);
                    ModuloVideoContinuo.Instance.ActualizarTurno(_turno);

                    _logicaVia.GetVehOnline().CategoriaProximo = 0;
                    _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                    // Adelantar Vehiculo
                    _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                    // Update Online
                    ModuloEventos.Instance.ActualizarTurno(_turno);

                    {
                        ImprimiendoTicket = true;
                        errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.TarjetaChip, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);
                        ImprimiendoTicket = false;
                    }

                    UpdateOnline();
                    _logicaVia.GrabarVehiculos();
                }
                _logger.Debug("PagadoTarjetaChip -> Salir");
            }
            catch (Exception e)
            {
                _loggerExcepciones?.Error(e);
            }

            _ultimaAccion.Clear();
            _logicaVia.LoguearColaVehiculos();
        }

        /// <summary>
        /// Limpia los datos leídos de la tarjeta chip
        /// </summary>
        /// <param name="vehiculo"></param>
        private void ClearChip(Vehiculo vehiculo)
        {
            // Si el vehiculo tiene un tag asignado
            if (!string.IsNullOrEmpty(vehiculo.InfoTag.NumeroTag))
            {
                _logger.Debug("ClearChip -> Se limpian los datos leídos del veh {0}", vehiculo.NumeroVehiculo);
                vehiculo.InfoTag.Clear();
            }

            vehiculo.TipOp = ' ';
            vehiculo.Fecha = DateTime.MinValue;
            vehiculo.TipBo = ' ';
            vehiculo.Patente = "";
            vehiculo.TarifaOriginal = 0;
            vehiculo.TipoTarifa = 0;
            vehiculo.FormaPago = 0;
            vehiculo.EsperaRecargaVia = false;

            // Se envia a pantalla para mostrar los datos del vehiculo
            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

            // Limpio los mensajes de las lineas por si quedo algo de antes
            ModuloPantalla.Instance.LimpiarMensajes();
            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Categoria Tabulada") + ": " + vehiculo.CategoDescripcionLarga);

            // Se envia mensaje al display
            ModuloDisplay.Instance.Enviar(eDisplay.CAT, vehiculo);
        }

        public override void LeerTarjeta()
        {
            var task = Task.Run(async () => await ValidarPrecondicionesTagManual(eOrigenComando.Pantalla, eTipoLecturaTag.Chip));
            bool bLeer = task.Result;

            _logger.Info("LeerTarjeta -> Lazo Salida Egreso no está ocupado y hay un vehiculo categorizado, leemos TSC? [{0}]", bLeer && Estado == eEstadoVia.EVAbiertaCat ? "SI" : "NO");

            if (bLeer && Estado == eEstadoVia.EVAbiertaCat)
                ModuloTarjetaChip.Instance.IniciaLectura();
        }

        #endregion

        #region Tecla Alarma
        public void ProcesarTeclaAlarma(ComandoLogica comando)
        {
            Mimicos mimicos = new Mimicos();

            if (comando.Operacion == "Encender")
            {
                DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 100000, 100000, true);

                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;
            }
            else if (comando.Operacion == "Apagar")
            {
                DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 1, 1, true);

                mimicos.CampanaViolacion = enmEstadoAlarma.Ok;
            }

            List<DatoVia> listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);

            // Actualiza el estado de los mimicos en pantalla 
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
        }

        public async void RecibirAlarma(ComandoLogica comando)
        {
            enmEstadoAlarma estado = DAC_PlacaIO.Instance.ObtenerEstadoAlarma(eAlarma.Sonora);
            if (estado == enmEstadoAlarma.Activa)
            {
                DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 1, 1, true);

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                mimicos.CampanaViolacion = enmEstadoAlarma.Ok;

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, ModuloPantalla.Instance._ultimaPantalla, listaDatosVia);
            }
            else
            {
                DAC_PlacaIO.Instance.ActivarAlarma(eTipoAlarma.Violacion, 100000, 100000, true);

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                mimicos.CampanaViolacion = enmEstadoAlarma.Activa;

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, ModuloPantalla.Instance._ultimaPantalla, listaDatosVia);
            }
        }

        #endregion

        #region Comitiva

        public bool ValidarPrecondicionesComitiva()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado != eEstadoVia.EVAbiertaLibre && _estado != eEstadoVia.EVAbiertaCat && _estado != eEstadoVia.EVAbiertaPag)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);
                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.CobroManual))
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                retValue = false;
            }
            else if (vehiculo.EstaPagado)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PrimerVehiculoPagado);
                retValue = false;
            }
            else if (!ModoPermite(ePermisosModos.CobroManualSobreLazo) && _logicaVia.EstaOcupadoSeparadorSalida())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                retValue = false;
            }
            else if (EsCambioJornada())
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioJornada);
                retValue = false;
            }
            //vuelvo a traer el veh antes de validar esta precondición
            vehiculo = _logicaVia.GetPrimerVehiculo();
            if (vehiculo.ProcesandoViolacion)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ProcesandoViolacion);
                retValue = false;
            }

            return retValue;
        }

        /// <summary>
        /// Recibe la cantidad de ejes de la pantalla para categorias con mas de 10 ejes
        /// </summary>
        public async void ProcesarCategoEspecial(ComandoLogica comando)
        {
            if (comando == null || comando.Accion == enmAccion.T_CATEGORIAESPECIAL && comando.CodigoStatus != enmStatus.Abortada)
            {
                Vehiculo vehiculoPantalla = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);
                if (vehiculoPantalla.Categoria >= 10 && vehiculoPantalla.Categoria <= 20)
                    await Categorizar(vehiculoPantalla.Categoria, true);
            }
            if (comando.Accion == enmAccion.T_CATEGORIAESPECIAL && comando.CodigoStatus == enmStatus.Abortada)
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }
        public async void RecibirComitiva(ComandoLogica comando)
        {
            if (comando == null || comando.Accion == enmAccion.T_COMITIVA)
                await ProcesarComitiva(eTipoComando.eTecla);
            else if (comando.Accion == enmAccion.COMITIVA)
            {
                ListadoInfoTicketManual listaTicketManual = ClassUtiles.ExtraerObjetoJson<ListadoInfoTicketManual>(comando.Operacion);
                Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);

                await ProcesarComitiva(eTipoComando.eValidacion, listaTicketManual, causa);
            }
            else if (comando.Accion == enmAccion.COMITIVA_PARCIAL)
            {
                ListadoInfoTicketManual listaTicketManual = ClassUtiles.ExtraerObjetoJson<ListadoInfoTicketManual>(comando.Operacion);
                Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comando.Operacion);

                await ProcesarComitivaParcial(eTipoComando.eValidacion, listaTicketManual, causa);
            }
        }

        /// <summary>
        /// Actualiza el valor parcial de la comitiva en efectivo al ir agregando vehiculos
        /// </summary>
        public async Task ProcesarComitivaParcial(eTipoComando tipoComando, ListadoInfoTicketManual listaTicketManual = null, Causa causaEfectivoExento = null)
        {
            int CantTotalCategoriasIngresadas = 0;
            decimal dTotal = 0;
            listaTicketManual.FechaDesde = Fecha;
            listaTicketManual.FechaHasta = Fecha.AddMinutes(0.1);


            foreach (var tm in listaTicketManual.ListaTicketManual)
            {
                if (tm.Cantidad > 0)
                {
                    TarifaABuscar tarifa = GenerarTarifaABuscar(tm.Categoria, 0);
                    CantTotalCategoriasIngresadas += tm.Cantidad;
                    Tarifa tarif = new Tarifa();
                    tarif = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifa);
                    tm.CategoriaValor = tarif.Valor;
                    dTotal += tm.CategoriaValor * tm.Cantidad;
                }
            }
            if (causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Tarifa: {ClassUtiles.FormatearMonedaAString(dTotal)}");
        }

        /// <summary>
        /// Confirma la tarifa y la muestra en pantalla, llama al metodo que realiza el cobro
        /// </summary>
        public async Task ProcesarComitiva(eTipoComando tipoComando, ListadoInfoTicketManual listaTicketManual = null, Causa causaEfectivoExento = null)
        {
            if (ValidarPrecondicionesComitiva())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    List<DatoVia> listaDatosVia = new List<DatoVia>();
                    Causa causa = new Causa();
                    causa.Codigo = causaEfectivoExento.Codigo;
                    causa.Descripcion = causa.Codigo.GetDescription();
                    ListadoInfoTicketManual listaTicketManualPantalla = new ListadoInfoTicketManual();

                    listaTicketManualPantalla.ListaTicketManual = await ModuloBaseDatos.Instance.BuscarListaCatDescAsync();

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);
                    ClassUtiles.InsertarDatoVia(listaTicketManualPantalla, ref listaDatosVia);
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_COMITIVA, listaDatosVia);
                }
                if (tipoComando == eTipoComando.eValidacion)
                {
                    int CantTotalCategoriasIngresadas = 0;
                    decimal dTotal = 0;
                    listaTicketManual.FechaDesde = Fecha;
                    listaTicketManual.FechaHasta = Fecha.AddMinutes(0.1);


                    foreach (var tm in listaTicketManual.ListaTicketManual)
                    {
                        if (tm.Cantidad > 0)
                        {
                            TarifaABuscar tarifa = GenerarTarifaABuscar(tm.Categoria, 0);
                            CantTotalCategoriasIngresadas += tm.Cantidad;
                            Tarifa tarif = new Tarifa();
                            tarif = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifa);
                            tm.CategoriaValor = tarif.Valor;
                            dTotal += tm.CategoriaValor * tm.Cantidad;
                        }
                    }

                    if (CantTotalCategoriasIngresadas != 0)
                    {
                        listaTicketManual.TotalTicket = ClassUtiles.FormatearMonedaAString(dTotal);

                        await ConfirmarComitiva(listaTicketManual, CantTotalCategoriasIngresadas, causaEfectivoExento);
                        if (causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, $"Tarifa Total: {ClassUtiles.FormatearMonedaAString(dTotal)}");
                        else
                            ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, "Tarifa Total: S/. 0");
                    }
                    else
                    {
                        ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.NoSeIngresaronCantidades);
                    }
                }
            }
            else
            {
                List<DatoVia> listaDatosVia2 = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia2);
            }
        }

        /// <summary>
        /// Realiza el cobro de la comitiva, envia los datos a la base, cambia el estado de la via e imprime el ticket si corresponde
        /// </summary>
        private async Task ConfirmarComitiva(ListadoInfoTicketManual listaTicketManual, int cantVehiculos, Causa causaEfectivoExento)
        {
            InfoCliente cliente = new InfoCliente();
            InfoPagado oPagado;
            EnmErrorImpresora errorImpresora;
            Vehiculo vehiculo;
            vehiculo = _logicaVia.GetPrimerVehiculo();
            ulong NroVehiculo = vehiculo.NumeroVehiculo;
            oPagado = vehiculo.InfoPagado;
            vehiculo.CobroEnCurso = true;

            //Capturo foto y video
            _logicaVia.DecideCaptura(causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo ? eCausaVideo.Pagado : eCausaVideo.PagadoExento, vehiculo.NumeroVehiculo);

            //Tarifa auxiliar para setear tipo de dia, sino llega NULL
            TarifaABuscar tarifa = GenerarTarifaABuscar(2, 0);
            Tarifa tarif = new Tarifa();
            tarif = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifa);

            _logger.Info($"ConfirmarComitiva -> NroVeh [{NroVehiculo}]");

            //Limpio la cuenta de peanas del DAC
            DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

            oPagado.NoPermiteTag = true;
            oPagado.TipOp = causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo ? 'E' : 'X';
            oPagado.TipBo = ' ';
            oPagado.FormaPago = causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo ? eFormaPago.CTEfec : eFormaPago.CTExen;
            oPagado.Fecha = Fecha;
            oPagado.FechaFiscal = Fecha;
            oPagado.TipoDiaHora = tarif.CodigoHorario;

            cliente.RazonSocial = "CONSUMIDOR FINAL";
            cliente.Ruc = "9999999999999";
            oPagado.InfoCliente = cliente;

            //Finaliza lectura de Tchip
            ModuloTarjetaChip.Instance.FinalizaLectura();

            Comitiva DatosComitiva = new Comitiva();
            DatosComitiva.ListaTicketManual = listaTicketManual.ListaTicketManual;

            //Vuelvo a buscar el vehiculo
            if (NroVehiculo > 0)
            {
                vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));
                if (vehiculo.NumeroVehiculo != NroVehiculo)
                    vehiculo = _logicaVia.GetPrimerVehiculo();
            }
            else
                vehiculo = _logicaVia.GetPrimerVehiculo();

            //Si es comitiva efectivo, se imprime ticket
            if (causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
            {
                vehiculo.CargarDatosPago(oPagado, true);//se cargan los datos necesarios para generar clave                                   
                oPagado.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                if (_esModoMantenimiento)
                    oPagado.NumeroTicketNF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketNoFiscal);
                else
                    oPagado.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);

                vehiculo.infoTicketManuals = listaTicketManual.ListaTicketManual;

                errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                {
                    ImprimiendoTicket = true;

                    errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.ComitivaEfectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia);

                    ImprimiendoTicket = false;
                }

                //Vuelvo a buscar el vehiculo
                if (NroVehiculo > 0)
                {
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));
                    if (vehiculo.NumeroVehiculo != NroVehiculo)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                else
                    vehiculo = _logicaVia.GetPrimerVehiculo();
            }
            else
                errorImpresora = EnmErrorImpresora.SinFalla;

            if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
            {
                //verificar que el veh sigue disponible, si no lo está asignamos el pagado al veh de atras
                if (vehiculo.ProcesandoViolacion)
                {
                    _logger.Debug($"ConfirmarComitiva -> Se está procesando una violación en el veh [{vehiculo.NumeroVehiculo}]");
                    vehiculo.LimpiarDatosClave();
                    vehiculo = _logicaVia.GetPrimerVehiculo();
                    //Actualizo el numero de vehiculo
                    NroVehiculo = vehiculo.NumeroVehiculo;
                    _logger.Debug($"ConfirmarComitiva -> Se agregan datos del Pagado al vehiculo siguiente, veh [{vehiculo.NumeroVehiculo}]");
                }

                _estado = eEstadoVia.EVAbiertaPag;
                vehiculo.CargarDatosPago(oPagado, false);

                _loggerTransitos?.Info($"P;{oPagado.Fecha.ToString("HH:mm:ss.ff")};{oPagado.Categoria};{oPagado.TipOp};{oPagado.TipBo};{vehiculo.GetSubFormaPago()};{oPagado.Tarifa};{oPagado.NumeroTicketF};{vehiculo.Patente};{vehiculo.InfoTag.NumeroTag};{oPagado.InfoCliente.Ruc};0");

                //Cargo los vehiculos que van a pasar
                DatosComitiva.ModoComitiva = true;
                DatosComitiva.PrimeroComitiva = true;
                DatosComitiva.VehComitivaPendientes = cantVehiculos;
                DatosComitiva.ListaTicketManual = listaTicketManual.ListaTicketManual;
                DatosComitiva.VehComitivaTotal = DatosComitiva.VehComitivaPendientes;
                DatosComitiva.NumeroTicketComitiva = _esModoMantenimiento ? oPagado.NumeroTicketNF : oPagado.NumeroTicketF;
                DatosComitiva.FechaFiscal = Fecha;
                _logicaVia.ComitivaVehPendientes = DatosComitiva.VehComitivaTotal;
                if (causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
                {
                    //Genero el ticket legible 
                    await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.ComitivaEfectivo, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null, true);
                    DatosComitiva.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                }

                // Se actualizan perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                _logger?.Debug("ConfirmarComitiva -> BARRERA ARRIBA!!");

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Actualiza el mensaje en pantalla
                if (causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoEfectivo);
                else
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagadoExento);

                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, "Comitiva de" + " " + cantVehiculos + " " + "Vehiculos confirmada");
                _logger.Info($"ConfirmarComitiva -> Comitiva de {0} vehiculos confirmada", cantVehiculos);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.ESTADO_SUB, null);

                // Actualiza el estado de vehiculo en pantalla
                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                ClassUtiles.InsertarDatoVia(cliente, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                // Envia mensaje a display
                ModuloDisplay.Instance.Enviar(causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo ? eDisplay.PAG : eDisplay.EXE);

                
                //almacena la foto
                ModuloFoto.Instance.AlmacenarFoto(ref vehiculo);

                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                // Adelantar Vehiculo
                _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                //Vuelvo a buscar el vehiculo
                if (NroVehiculo > 0)
                {
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));

                    if (vehiculo.NumeroVehiculo != NroVehiculo)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                else
                {
                    vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTransito);

                Vehiculo veh = null;

                foreach (InfoTicketManual it in listaTicketManual.ListaTicketManual)
                {
                    if (it.Cantidad > 0)
                    {
                        veh = new Vehiculo();
                        veh.TipOp = causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo ? 'E' : 'X';
                        veh.TipBo = ' ';
                        veh.Tarifa = it.Cantidad * it.CategoriaValor;
                        veh.Categoria = it.Categoria;
                        veh.InfoDac.Tarifa = 0;
                        veh.InfoDac.Categoria = 0;
                        veh.PosComitiva = (byte)(DatosComitiva.VehComitivaTotal - DatosComitiva.VehComitivaPendientes + 1);
                        veh.Reversa = false;
                        veh.NoPermiteTag = true;
                        veh.FormaPago = veh.TipOp == 'X' ? eFormaPago.CTExen : eFormaPago.CTEfec;
                        veh.Fecha = Fecha;
                        veh.FechaFiscal = DatosComitiva.FechaFiscal;
                        veh.TicketLegible = DatosComitiva.TicketLegible;
                        if (_esModoMantenimiento)
                            veh.NumeroTicketNF = DatosComitiva.NumeroTicketComitiva;
                        else
                            veh.NumeroTicketF = DatosComitiva.NumeroTicketComitiva;

                        for (int i = 0; i < it.Cantidad; i++)
                        {
                            veh.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);
                            ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroVehiculo);
                            if (causaEfectivoExento.Codigo == eCausas.ComitivaExento)
                                ModuloBaseDatos.Instance.AlmacenarAnomaliaTurno(eAnomalias.Franquicia);
                            //Almacenar Transitos 
                            ModuloBaseDatos.Instance.AlmacenarTransitoTurno(veh, _turno, it.Cantidad);
                        }

                        //Almacenar todos los efectivos
                        ModuloBaseDatos.Instance.AlmacenarPagadoTurno(veh, _turno, it.Cantidad);
                    }
                }

                //Envío evento de SetComitivaXML
                ConfirmarVehiculo(NroVehiculo, ref vehiculo);
                if(causaEfectivoExento.Codigo == eCausas.ComitivaEfectivo)
                {
                    Vehiculo vehXML = new Vehiculo();
                    vehXML.Tarifa = DatosComitiva.ImporteTotal;
                    vehXML.TipOp = 'E';
                    vehXML.NumeroTicketF = DatosComitiva.NumeroTicketComitiva;
                    vehXML.FechaFiscal = DatosComitiva.FechaFiscal;
                    vehXML.Fecha = vehXML.FechaFiscal;
                    vehXML.Patente = "COMITIVA";
                    vehXML.IVA = vehXML.Tarifa * 18 / 118;
                    vehXML.IVA = Decimal.Round(vehXML.IVA, 2);

                    ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, vehXML.InfoCliente, vehXML);
                    vehiculo.XMLFactura = vehXML.XMLFactura;
                    vehiculo.XMLNombreFactura = vehXML.XMLNombreFactura;
                }

                ModuloEventos.Instance.SetComitivaXML(_turno, vehiculo, listaTicketManual, DatosComitiva);

                // Update Online
                UpdateOnline();
                ModuloEventos.Instance.ActualizarTurno(_turno);

                vehiculo.CobroEnCurso = false;
                _logicaVia.GrabarVehiculos();
            }
            else
            {
                vehiculo.CobroEnCurso = false;
                vehiculo.LimpiarDatosClave();
                oPagado.ClearDatosFormaPago();
                _logicaVia.GrabarVehiculos();

                if (_esModoMantenimiento)
                {
                    ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                }
                else
                {
                    ulong ulNro = ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                    _logger.Debug($"ConfirmarComitiva -> Decrementa nro ticket. Actual [{ulNro}]");
                }

                // Envia el error de impresora a pantalla
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                _logger.Info("ConfirmarComitiva -> " + Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
            }
            _logicaVia.LoguearColaVehiculos();
            _ultimaAccion.Clear();

            List<DatoVia> listaDatosVia3 = new List<DatoVia>();
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia3);

        }
        #endregion

        #region Encuesta Usuarios

        public async void RecibirEncuesta(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Abortada)
                await ProcesarEncuesta(eTipoComando.eTecla, null);
            else if (comando.CodigoStatus == enmStatus.Ok)
            {
                await ProcesarEncuesta(eTipoComando.eConfirmacion, comando);
            }
        }

        /// <summary>
        /// Recibe la encuesta de pantalla y arma el XML para enviar a la base de datos
        /// </summary>
        public Task ProcesarEncuesta(eTipoComando tipoComando, ComandoLogica comando)
        {
            if (tipoComando == eTipoComando.eTecla)
            {
                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
            }
            if (tipoComando == eTipoComando.eConfirmacion)
            {
                string sRespuestaElegida = comando.Operacion;
                Encuesta encuesta = JsonConvert.DeserializeObject<Encuesta>(sRespuestaElegida);
                StringBuilder builder = new StringBuilder();
                builder.Append("<Encuesta>");
                foreach (var tm in encuesta.ListaPreguntas)
                {
                    builder.AppendFormat("<Encuesta");
                    builder.AppendFormat(" Pregunta= \"{0}\" ", tm.Codigo);
                    foreach (var dm in tm.ListaRespuestas)
                    {
                        if (dm.Seleccionada == true)
                        {
                            builder.AppendFormat(" Respuesta= \"{0}\"", dm.Codigo);
                        }
                    }
                    builder.Append("/>");
                }
                builder.Append("</Encuesta>");
                ModuloEventos.Instance.SetEncuestaXML(ModuloBaseDatos.Instance.ConfigVia, _turno, builder, encuesta);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, listaDatosVia);
                ModuloPantalla.Instance.LimpiarMensajes();
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, "Encuesta Finalizada");

            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Recibe las preguntas filtradas por fecha y categoria y arma la encuesta para enviar a pantalla
        /// </summary>
        public void MostrarEncuesta(List<ListadoEncuestas> encuestas, Vehiculo vehiculo)
        {
            var encuestaFinal = new Encuesta
            {
                Categoria = encuestas[0].Categoria,
                Codigo = (int)(encuestas[0].Codigo),
                FechaFin = encuestas[0].FechaFin.ToString(),
                FechaIni = encuestas[0].FechaIni.ToString(),
                Titulo = encuestas[0].Titulo,
                ListaPreguntas = encuestas
                         .GroupBy(x => x.PregCodigo)
                         .Select(x => new ListadoPreguntas
                         {
                             Codigo = x.Key,
                             Descripcion = x.FirstOrDefault()?.PregDescripcion,
                             EncuestaID = (int)(x.FirstOrDefault()?.Codigo),
                             ListaRespuestas = x
                                 .Select(y => new ListadoRespuestas
                                 {
                                     Codigo = y.RespCodigo,
                                     Descripcion = y.RespDescripcion
                                 })
                                 .ToList()
                         })
                         .ToList()
            };

            List<DatoVia> listaDatosVia2 = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(encuestaFinal, ref listaDatosVia2);
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia2);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_ENCUESTA, listaDatosVia2);
        }

        #endregion

        #region Pago Diferido
        public async void CrearPagoDiferido(ComandoLogica comando)
        {
            if (comando.CodigoStatus == enmStatus.Abortada)
                await ProcesarPagoDiferido(eTipoComando.eTecla, null);
            else if (comando.CodigoStatus == enmStatus.Ok)
            {
                Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);
                await ProcesarPagoDiferido(eTipoComando.eConfirmacion, vehiculo);
            }
        }

        public async void GenerarPagoDiferido(ComandoLogica comando)
        {
            if (comando == null)
                await ProcesarPagoDiferido(eTipoComando.eTecla);
            else if(comando.CodigoStatus == enmStatus.Tecla )
                await ProcesarPagoDiferido(eTipoComando.eTecla);
            else if (comando.Accion == enmAccion.CREAR_PAGO_DIF)
            {
                Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(comando.Operacion);

                await ProcesarPagoDiferido(eTipoComando.eConfirmacion, vehiculo);
            }
        }

        public async Task ProcesarPagoDiferido(eTipoComando tipoComando, Vehiculo vehiculo = null)
        {
            if (ValidarPrecondicionesPagoDiferido())
            {
                if (tipoComando == eTipoComando.eTecla)
                {
                    vehiculo = _logicaVia.GetVehIng();
                    List<InfoDeuda> listaDeudas = new List<InfoDeuda>();
                    List<Diferido> listaPagosDifPendientes = await ModuloBaseDatos.Instance.BuscarPagosDiferidosAsync(vehiculo.Patente);
                    List<Violacion> listaViolaciones = await ModuloBaseDatos.Instance.BuscarViolacionesAsync(vehiculo.Patente);
                    InfoDeuda deuda;

                    if (listaPagosDifPendientes.Count != 0)
                    {
                        foreach (Diferido item in listaPagosDifPendientes)
                        {
                            deuda = new InfoDeuda();
                            deuda.Tipo = eTipoDeuda.PagoDiferido;
                            deuda.Categoria = item.CategoriaGeneracion;
                            deuda.FechaHora = item.FechaGeneracion.GetValueOrDefault();
                            deuda.Estacion = (short)item.EstacionGeneracion;
                            deuda.Monto = item.ImporteGenerado.GetValueOrDefault();
                            deuda.DescripcionCategoria = item.DescripcionCategoria;
                            deuda.NombreEstacion = item.NombreEstacion;
                            deuda.Via = item.ViaGeneracion;
                            deuda.Id = item.NumeroPagoDiferido;
                            listaDeudas.Add(deuda);
                        }
                    }

                    if (listaViolaciones.Count != 0)
                    {
                        foreach (Violacion item in listaViolaciones)
                        {
                            deuda = new InfoDeuda();
                            deuda.Tipo = eTipoDeuda.Violacion;
                            deuda.Categoria = item.CategoriaGeneracion;
                            deuda.FechaHora = item.FechaGeneracion;
                            deuda.Estacion = (short)item.EstacionGeneracion;
                            deuda.Monto = item.ImporteGenerado;
                            deuda.DescripcionCategoria = item.DescripcionCategoria;
                            deuda.NombreEstacion = item.NombreEstacion;
                            deuda.Via = item.ViaGeneracion;
                            deuda.Id = 0;
                            listaDeudas.Add(deuda);
                        }
                    }

                    if (listaDeudas.Count != 0)
                    {
                        vehiculo.ListaInfoDeuda = listaDeudas;
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(listaDeudas, ref listaDatosVia);
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.PAGO_DIF, listaDatosVia);
                    }
                    else
                    {
                        List<DatoVia> listaDatosVia = new List<DatoVia>();
                        ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                        ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.PAGO_DIF, listaDatosVia);
                    }
                }
                else if (tipoComando == eTipoComando.eConfirmacion)
                {
                    await ConfirmarPagoDiferido(vehiculo);
                    List<DatoVia> listaDatosVia2 = new List<DatoVia>();ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_CATEGORIAS, new List<DatoVia>());
                }
            }
            else 
            {
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.T_FORMAPAGO, new List<DatoVia>());
            }
        }

        private async Task ConfirmarPagoDiferido(Vehiculo vehiculoDeuda)
        {
            EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);
            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            List<DatoVia> listaDatosVia = new List<DatoVia>();

            _logicaVia.GetPrimerVehiculo().Patente = vehiculo.Patente;

            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(_logicaVia.GetPrimerVehiculo(), ref listaDatosVia);
            ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

            listaDatosVia = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);

            bool MostrarPantalla = true;

            DeudaPagoDiferido deudaPagoDiferido = new DeudaPagoDiferido();

            List<Diferido> listaPagosDifPendientes = await ModuloBaseDatos.Instance.BuscarPagosDiferidosAsync(vehiculo.Patente);
            deudaPagoDiferido.CantPagosDiferidos = listaPagosDifPendientes.Count;

            List<Violacion> listaViolaciones = await ModuloBaseDatos.Instance.BuscarViolacionesAsync(vehiculo.Patente);
            deudaPagoDiferido.CantViolaciones = listaViolaciones.Count;

            // No tiene deuda y no usa el documento
            if (deudaPagoDiferido.CantPagosDiferidos == 0 && deudaPagoDiferido.CantViolaciones == 0)
            {
                // Mostrar mensaje de confirmacion
                Causa causa = new Causa(eCausas.PagoDiferido, eCausas.PagoDiferido.GetDescription());

                ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
            }
            else
            {
                int maxCantDeudasPagoDif = ModuloBaseDatos.Instance.ConfigVia.MaxCantDeudasPagoDif;
                maxCantDeudasPagoDif = 3;   //hardcodeado hasta tener el parametro en la base

                // Tiene deuda mayor a la maxima
                if (deudaPagoDiferido.CantPagosDiferidos > maxCantDeudasPagoDif ||
                    deudaPagoDiferido.CantViolaciones > maxCantDeudasPagoDif)
                {
                    MostrarPantalla = false;
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("No es posible realizar el Pago Diferido"));
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, string.Format(Traduccion.Traducir("Cantidad de deudas supera el máximo permitido ({0})"), maxCantDeudasPagoDif));
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea3, string.Format(Traduccion.Traducir("Deudas Pago Diferido: {0}, Violaciones: {1}"), deudaPagoDiferido.CantPagosDiferidos, deudaPagoDiferido.CantViolaciones));
                }
                else
                {
                    int horasVencimientoPagoDif = ModuloBaseDatos.Instance.ConfigVia.VencimientoPagosDif.GetValueOrDefault();

                    foreach (var pagoDifPendiente in listaPagosDifPendientes)
                    {
                        // Si esta vencido, se cobra el valor actual
                        if (horasVencimientoPagoDif != 0 &&
                            (pagoDifPendiente.FechaVencimiento - pagoDifPendiente.FechaGeneracion).GetValueOrDefault().TotalHours > horasVencimientoPagoDif)
                        {
                            Vehiculo vehiculoActual = _logicaVia.GetPrimerVehiculo();
                            deudaPagoDiferido.MontoTotalPagosDiferidos = vehiculoActual.Tarifa;
                        }
                        else // Si no esta vencido, se cobra el valor original
                        {
                            deudaPagoDiferido.MontoTotalPagosDiferidos += pagoDifPendiente.ImporteGenerado.GetValueOrDefault();
                        }
                    }

                    float multiplicadorTarifaViolaciones = ModuloBaseDatos.Instance.ConfigVia.MultiplicadorTarifaViolaciones.GetValueOrDefault();

                    foreach (var violacion in listaViolaciones)
                        deudaPagoDiferido.MontoTotalViolaciones += violacion.ImporteGenerado * (Decimal)multiplicadorTarifaViolaciones;

                    ClassUtiles.InsertarDatoVia(deudaPagoDiferido, ref listaDatosVia);
                }
                if (MostrarPantalla)
                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CREAR_PAGO_DIF, listaDatosVia);
            }
            ImprimirTicketPagoDiferido();
            GenerarPagoDiferido();

        }

        private async void ImprimirTicketPagoDiferido()
        {
            Vehiculo vehiculo;
            InfoPagado oPagado;
            ulong NroVehiculo = 0;

            vehiculo = _logicaVia.GetPrimerVehiculo();
            oPagado = vehiculo.InfoPagado;
            NroVehiculo = vehiculo.NumeroVehiculo;

            // Se busca la tarifa
            TarifaABuscar tarifaABuscar = GenerarTarifaABuscar(vehiculo.Categoria, 0);
            Tarifa tarifa = await ModuloBaseDatos.Instance.BuscarTarifaAsync(tarifaABuscar);

            oPagado.Tarifa = tarifa.Valor;
            oPagado.DesCatego = tarifa.Descripcion;
            oPagado.CategoDescripcionLarga = tarifa.Descripcion;
            oPagado.TipoDiaHora = tarifa.CodigoHorario;

            if (oPagado.Tarifa > 0)
            {
                //Vuelvo a buscar el vehiculo
                if (NroVehiculo > 0)
                {
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));
                    if (vehiculo.NumeroVehiculo != NroVehiculo)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                else
                    vehiculo = _logicaVia.GetPrimerVehiculo();

                oPagado.NoPermiteTag = true;
                oPagado.TipOp = 'D';
                oPagado.TipBo = ' ';
                oPagado.FormaPago = eFormaPago.CTEfec;
                oPagado.Fecha = Fecha;
                oPagado.FechaFiscal = Fecha;

                vehiculo.CargarDatosPago(oPagado, true);//se cargan los datos necesarios para generar clave
                oPagado.ClaveAcceso = ClassUtiles.GenerarClaveAcceso(vehiculo, ModuloBaseDatos.Instance.ConfigVia, _turno);

                EnmErrorImpresora errorImpresora = ModuloImpresora.Instance.ImprimirTicket(true, enmFormatoTicket.EstadoImp);

                if (errorImpresora == EnmErrorImpresora.SinFalla || errorImpresora == EnmErrorImpresora.PocoPapel)
                {
                    ImprimiendoTicket = true;

                    errorImpresora = await ModuloImpresora.Instance.ImprimirTicketAsync(true, enmFormatoTicket.PagoDiferido, vehiculo, _turno, ModuloBaseDatos.Instance.ConfigVia, null);

                    ImprimiendoTicket = false;
                }

                //Vuelvo a buscar el vehiculo
                if (NroVehiculo > 0)
                {
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));
                    if (vehiculo.NumeroVehiculo != NroVehiculo)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                else
                    vehiculo = _logicaVia.GetPrimerVehiculo();

                ModuloPantalla.Instance.LimpiarMensajes();

                //Si falla la impresion, se limpian los datos correspondientes del vehiculo
                if (errorImpresora != EnmErrorImpresora.SinFalla && errorImpresora != EnmErrorImpresora.PocoPapel
                && !ModoPermite(ePermisosModos.TransitoSinImpresora))
                {
                    vehiculo.NoPermiteTag = false;
                    vehiculo.CobroEnCurso = false;
                    vehiculo.TipOp = ' ';
                    vehiculo.FormaPago = eFormaPago.Nada;
                    vehiculo.Tarifa = 0;

                    if (_esModoMantenimiento)
                    {
                        vehiculo.NumeroTicketNF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketNoFiscal);
                    }
                    else
                    {
                        vehiculo.NumeroTicketF = 0;
                        ModuloBaseDatos.Instance.DecrementarContador(eContadores.NumeroTicketFiscal);
                    }

                    // Envia el error de impresora a pantalla
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, Traduccion.Traducir("Error Impresora") + ": " + Traduccion.Traducir(ClassUtiles.GetEnumDescr(errorImpresora)));
                }
                else
                {                    
                    List<DatoVia> listaDatosVia = new List<DatoVia>();

                    // Mostrar mensaje de confirmacion
                    Causa causa = new Causa(eCausas.PagoDiferido, eCausas.PagoDiferido.GetDescription());

                    ClassUtiles.InsertarDatoVia(causa, ref listaDatosVia);

                    ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.CONFIRMAR, listaDatosVia);
                }
            }
            else
            {
                vehiculo.CobroEnCurso = false;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TarifaErrorBusqueda);
                _logger.Info("ImprimirTicketPagoDiferido -> Hubo un problema al consultar la tarifa");
            }
        }

        private void GenerarPagoDiferido()
        {
            Vehiculo vehiculo;
            InfoPagado oPagado;
            ulong NroVehiculo = 0;
            InfoCliente cliente = new InfoCliente();

            vehiculo = _logicaVia.GetPrimerVehiculo();
            oPagado = vehiculo.InfoPagado;
            NroVehiculo = vehiculo.NumeroVehiculo;
            vehiculo.CobroEnCurso = true;

            //Capturo foto y video
            _logicaVia.DecideCaptura(eCausaVideo.Pagado, vehiculo.NumeroVehiculo);

            if (oPagado.Tarifa > 0)
            {
                //Limpio la cuenta de peanas del DAC
                DAC_PlacaIO.Instance.NuevoTransitoDAC(oPagado.Categoria, _logicaVia.EstaOcupadoBucleSalida() ? false : true);

                //Vuelvo a buscar el vehiculo
                if (NroVehiculo > 0)
                {
                    vehiculo = _logicaVia.GetVehiculo(_logicaVia.BuscarVehiculo(NroVehiculo, false, true));
                    if (vehiculo.NumeroVehiculo != NroVehiculo)
                        vehiculo = _logicaVia.GetPrimerVehiculo();
                }
                else
                    vehiculo = _logicaVia.GetPrimerVehiculo();

                Vehiculo oVehAux = new Vehiculo();

                //verificar que el veh sigue disponible, si no lo está asignamos el pagado al veh de atras
                if (vehiculo.ProcesandoViolacion)
                {
                    _logger.Debug($"GenerarPagoDiferido -> Se está procesando una violación en el veh [{vehiculo.NumeroVehiculo}]");
                    vehiculo.LimpiarDatosClave();
                    vehiculo = _logicaVia.GetPrimerVehiculo();
                    //Actualizo el numero de vehiculo
                    NroVehiculo = vehiculo.NumeroVehiculo;
                    _logger.Debug($"GenerarPagoDiferido -> Se agregan datos del Pagado al vehiculo siguiente, veh [{vehiculo.NumeroVehiculo}]");
                }

                //Finaliza lectura de Tchip
                ModuloTarjetaChip.Instance.FinalizaLectura();

                oPagado.NoPermiteTag = true;
                oPagado.TipOp = 'D';
                oPagado.TipBo = ' ';
                oPagado.FormaPago = eFormaPago.CTEfec;
                oPagado.Fecha = Fecha;
                oPagado.FechaFiscal = Fecha;

                _estado = eEstadoVia.EVAbiertaPag;
                cliente.RazonSocial = "CONSUMIDOR FINAL";
                cliente.Ruc = "9999999999999";
                oPagado.InfoCliente = cliente;
                oPagado.NumeroTicketF = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTicketFiscal);

                vehiculo.TicketLegible = ModuloImpresora.Instance.UltimoTicketLegible;
                //Se incrementa tránsito
                vehiculo.NumeroTransito = ModuloBaseDatos.Instance.IncrementarContador(eContadores.NumeroTransito);

                //se cargan los datos al vehiculo, vehAux se utiliza para las operaciones
                vehiculo.CargarDatosPago(oPagado);
                vehiculo.FechaGeneracionPagoDiferido = DateTime.Now;
                vehiculo.FechaPagoDiferido = DateTime.Now.AddDays(90);
                oVehAux.CopiarVehiculo(ref vehiculo);

                // Se actualizan perifericos
                DAC_PlacaIO.Instance.SemaforoPaso(eEstadoSemaforo.Verde);
                DAC_PlacaIO.Instance.AccionarBarrera(eEstadoBarrera.Sube, eTipoBarrera.Via);
                _logger?.Debug("GenerarPagoDiferido -> BARRERA ARRIBA!!");

                // Actualiza el estado de los mimicos en pantalla
                Mimicos mimicos = new Mimicos();
                DAC_PlacaIO.Instance.EstadoPerifericos(ref mimicos);

                List<DatoVia> listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(mimicos, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.MIMICOS, listaDatosVia);

                // Actualiza el mensaje en pantalla
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.PagoDiferido);

                _logger.Info($"GenerarPagoDiferido -> NroVeh [{NroVehiculo}]");

                listaDatosVia = new List<DatoVia>();
                ClassUtiles.InsertarDatoVia(vehiculo, ref listaDatosVia);
                ModuloPantalla.Instance.EnviarDatos(enmStatus.Ok, enmAccion.DATOS_VEHICULO, listaDatosVia);

                
                //almacena la foto
                ModuloFoto.Instance.AlmacenarFoto(ref oVehAux);
                // Envia mensaje a display
                ModuloDisplay.Instance.Enviar(eDisplay.PAG);
                //Almacenar transito en bd local
                ModuloBaseDatos.Instance.AlmacenarPagadoTurno(oVehAux, _turno);
                //Se envía setCobro
                oVehAux.Operacion = "CB";
                ModuloImpresora.Instance.EditarXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux.InfoCliente, oVehAux);
                ModuloEventos.Instance.SetCobroXML(ModuloBaseDatos.Instance.ConfigVia, _turno, oVehAux);

                _logicaVia.GetVehOnline().CategoriaProximo = 0;
                _logicaVia.GetVehOnline().InfoDac.Categoria = 0;

                // Adelantar Vehiculo
                _logicaVia.AdelantarVehiculo(eMovimiento.eOpPago);

                // Update Online
                ModuloEventos.Instance.ActualizarTurno(_turno);
                UpdateOnline();
                _logicaVia.GrabarVehiculos();
                
            }
            else
            {
                vehiculo.CobroEnCurso = false;
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.TarifaErrorBusqueda);
                _logger.Info("GenerarPagoDiferido -> Hubo un problema al consultar la tarifa");
            }
        }

        public bool ValidarPrecondicionesPagoDiferido()
        {
            bool retValue = true;

            Vehiculo vehiculo = _logicaVia.GetPrimerVehiculo();

            if (_estado == eEstadoVia.EVCerrada)
            {
                ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ViaNoAbierta);

                retValue = false;
            }
            else
            {
                if (vehiculo.EstaPagado)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.EspereAQueAvance);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.CobroManual))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.ModoNoPermiteEstaOperacion);
                    retValue = false;
                }
                else if (vehiculo.Categoria <= 0 || _modo.Cajero != "S")
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoNoCategorizado + "o");
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea2, eMensajesPantalla.ModoConCajero);
                    retValue = false;
                }
                else if (!ModoPermite(ePermisosModos.CobroManualSobreLazo) && _logicaVia.EstaOcupadoBucleSalida())
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.BucleSalidaOcupado);
                    retValue = false;
                }
                else if (EsCambioTarifa(false))
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa);
                    retValue = false;
                }
                else if (_logicaVia.GetVehIng().Patente == string.Empty)
                {
                    ModuloPantalla.Instance.EnviarMensaje(enmTipoMensaje.Linea1, eMensajesPantalla.VehiculoSinPatente);
                    retValue = false;
                }
                //else if( La fecha actual es la misma que la fecha en que se abrió el turno )
                //{
                //    ModuloPantalla.Instance.EnviarMensaje( enmTipoMensaje.Linea1, eMensajesPantalla.CambioTarifa );
                //    retValue = false;
                //}
            }

            return retValue;
        }

        #endregion
    }
}
