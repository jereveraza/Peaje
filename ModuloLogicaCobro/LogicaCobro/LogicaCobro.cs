using Entidades;
using System;
using Entidades.Logica;
using Entidades.Comunicacion;
using Entidades.Interfaces;
using Entidades.ComunicacionBaseDatos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ModuloLogicaCobro.LogicaCobro
{
    public abstract class LogicaCobro : ILogicaCobro
    {
        protected bool _init;
        protected bool _esModoMantenimiento;
        protected bool _seConfirmoNum;
        protected eEstadoVia _estado;
        protected eEstadoEscape _estadoEscape;
        protected eEstadoValidacionVia _estadoValidacionVia;
        protected eQuiebre _modoQuiebre;
        protected Modos _modo;
        protected eEstadoNumeracion _estadoNumeracion = eEstadoNumeracion.Ninguno;
        protected Vars _numeracion;

        public Modos Modo { get { return _modo; } set { _modo = value; } }

        public byte NumVia { get; set; }
        public byte NumEstacion { get; set; }
        protected Turno _turno = null;
        protected Dictionary<string, string> _dictionarioTeclas = new Dictionary<string,string>();
        public Dictionary<string, string> DiccionarioTeclado { get { return _dictionarioTeclas; } set { _dictionarioTeclas = value; } }
        protected DateTime Fecha;
        public DateTime FecUltimoTransito { get; set; }

        public Turno GetTurno { get { return _turno; } }

        protected ulong _numBloque;

        protected ulong _numEve;
        protected ulong _numEveOnl;

        public abstract void SetLogicaVia(ILogicaVia logicaVia);

        public abstract void SetEstadoValidacionVia(eEstadoValidacionVia estadoValidacionVia);

        //public abstract void LimpiarTagsLeidos();

        public abstract void TeclaCategoria( ComandoLogica comando );

        public abstract void TeclaSubeBarrera( ComandoLogica comando );

        public abstract void TeclaBajaBarrera( ComandoLogica comando );

        public abstract void TeclaMenu( ComandoLogica comando );

        public abstract void TeclaQuiebreBarrera( ComandoLogica comando );

        public abstract void TeclaTagManual( ComandoLogica comando );

        public abstract void TeclaFactura( ComandoLogica comando );

        public abstract void TeclaFoto( ComandoLogica comando );

        public abstract void TeclaMoneda( ComandoLogica comando );

        public abstract void TeclaObservacion( ComandoLogica comando );

        public abstract void TeclaVideoInterno(ComandoLogica comando);

        public abstract void TeclaUnViaje( ComandoLogica comando );

        public abstract void TeclaAperturaCierreTurno( ComandoLogica comando );

        public abstract void TeclaSemaforoMarquesina( ComandoLogica comando );

        public abstract void TeclaEnter( ComandoLogica comando );

        public abstract void CancelarTagPago( ComandoLogica comando );

        public abstract void TeclaPagoEfectivo( ComandoLogica comando );

        public abstract void TeclaTarjetaCredito(ComandoLogica comando);

        public abstract void TeclaCancelar( ComandoLogica comando );

        public abstract void TeclaDetracManual(ComandoLogica comando);

        public abstract void TeclaExento( ComandoLogica comando );

        public abstract void TeclaPatente( ComandoLogica comando );

        public abstract void TeclaSimulacion( ComandoLogica comando );

        public abstract void SimulacionPaso( CausaSimulacion causaSimulacion, bool bajarBarrera = true, ulong ulNumVeh = 0, eVehiculo eVeh = eVehiculo.eVehP1);

        public abstract void TeclaLiquidacion( ComandoLogica comando );

        public abstract Task CierreTurno(bool cierreAutomatico, eCodigoCierre codCierre, eOrigenComando origenComando );

        public abstract Task AperturaTurno( Modos modo, Operador operador, bool aperturaAutomatica, eOrigenComando origenComando, bool EnviarSetApertura = true, bool confirmar = false);

        public abstract eEstadoNumeracion GetEstadoNumeracion();
        public abstract void ImprimirTotales( ComandoLogica comando );

        public abstract bool GetUltimoSentidoEsOpuesto();

        public abstract int GetComitivaVehPendientes();

        public abstract eEstadoVia GetEstadoVia();


        public eEstadoVia Estado { get { return _estado; } set { _estado = value; } }
        public eEstadoEscape EstadoEscape { get { return _estadoEscape; } set { _estadoEscape = value; } }
        public eEstadoValidacionVia EstadoValidacionVia { get { return _estadoValidacionVia; } set { _estadoValidacionVia = value; } }
        public eQuiebre ModoQuiebre { get { return _modoQuiebre; } }

        public abstract void SetTurno(TurnoBD oTurno, bool bUltTurno, bool bInit);
        public abstract void TeclaRetiro( ComandoLogica comando );
        public abstract void ProcesarCredenciales( ComandoLogica comando );
        public abstract void ProcesarOpcionMenu( ComandoLogica comando );
        public abstract void ValidarCambioPassword( ComandoLogica comando );
        public abstract void SalvarRetiroAnticipado( ComandoLogica comando );
        public abstract void SalvarFondoDeCambio( ComandoLogica comando );
        public abstract void ProcesarPatenteIngresada( ComandoLogica comando );
        public abstract void ValidarFactura( ComandoLogica comando );
        public abstract void SalvarMoneda( ComandoLogica comando );
        public abstract void SalvarLiquidacion( ComandoLogica comando );
        public abstract void SalvarExento( ComandoLogica comando );
        public abstract void SalvarTagManual( ComandoLogica comando );
        public bool ImprimiendoTicket { get; set; }
        public abstract Task Categorizar(short categoria, bool mostrarMensajePantalla = true);
        public abstract void TeclaMenuVenta( ComandoLogica comando );
        public abstract void CargarDelegadosPantalla();
        public abstract void InicializarPantalla(bool bIniciaSinMensajes);
        public abstract void SetEstadoNumeracion( eEstadoNumeracion estadoNumeracion, Vars numeracion, bool bInit = true );
        public abstract bool ModoPermite(ePermisosModos permisoModo);
       // public abstract Task GeneraClearing(InfoPagado vehiculo, bool esComitiva );
        //public abstract void RecibirAP( ComandoLogica comando );
        public Comitiva Comitiva { set; get; }

        public abstract Operador GetOperador { get; }

        public abstract void Dispose();
        public abstract void TeclaEscape(ComandoLogica comando);
        public abstract void Cancelacion(CausaCancelacion causa, bool bAutomatico = false, ulong ulNumVeh = 0, eVehiculo eVeh = eVehiculo.eVehP1);
        public abstract void LeerTarjeta();

    }
}
