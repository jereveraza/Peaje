using Entidades.Interfaces;
using Entidades.Logica;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entidades;
using Entidades.ComunicacionAntena;
using Entidades.ComunicacionBaseDatos;

namespace ModuloLogicaVia.Logica
{
    public abstract class LogicaVia : ILogicaVia
    {
        public abstract void SetLogicaCobro(ILogicaCobro LogicaCobro);

        public abstract void LazoSalidaIngreso(bool esModoForzado, short RespDAC);
        public abstract void LazoSalidaEgreso(bool esModoForzado, short RespDAC);
        public abstract void SeparadorSalidaIngreso(bool esModoForzado, short RespDAC);
        public abstract void SeparadorSalidaEgreso(bool esModoForzado, short RespDAC);
        public abstract void LazoPresenciaIngreso(bool esModoForzado, short RespDAC);
        public abstract void LazoPresenciaEgreso(bool esModoForzado, short RespDAC);
        public abstract void InicioColaOn(bool esModoForzado, short RespDAC);
        public abstract void InicioColaOff(bool esModoForzado, short RespDAC);
        public abstract void LazoEscapeIngreso();
        public abstract void LazoEscapeEgreso();
        public abstract void PulsadorEscape(short sEstado, bool bFromSupervision);
        //Funciones para pasar a Logica Cobro
        //devuelve el primer vehiculo NoVacio de la fila, no importa su condicion (siempre debe devolver un vehiculo)
        public abstract Vehiculo GetPrimerVehiculo();
        //Si el primer vehiculo está pagado y sobre el lazo devuelve el segundo de la fila, sino el primero (siempre debe devolver un vehiculo)
        public Vehiculo GetPrimeroSegundoVehiculo()
        {
            bool esPrimero = false;
            return GetPrimeroSegundoVehiculo( out esPrimero );
        }
        //Si el primer vehiculo está pagado y sobre el lazo devuelve el segundo de la fila, sino el primero (siempre debe devolver un vehiculo)
        public abstract Vehiculo GetPrimeroSegundoVehiculo(out bool esPrimero);
        //devuelve el ultimo vehiculo que ya salio
        public abstract Vehiculo GetVehiculoAnterior();
        //devuelve true si algun vehiculo de la fila esta pagado
        public abstract bool GetHayVehiculosPagados();
        public abstract Vehiculo GetVehiculo(eVehiculo eVehiculo);
        public abstract eVehiculo BuscarVehiculo(ulong ulNroVehiculo, bool buscarNoPagado = false, bool buscarHastaC0 = false, string sNroTag = "");
        public abstract Vehiculo GetVehIng(bool bSinPagadosEnBPA = false, bool bSinBPA = false, bool bSinPagados = false);
        public abstract Vehiculo GetVehIngCat();
        public abstract Vehiculo GetVehTran();
        public abstract Vehiculo GetVehAnt();
        public abstract Vehiculo GetVehVideo();
        public abstract Vehiculo GetVehEscape();
        public abstract Vehiculo GetVehOnline();
        public abstract Vehiculo GetVehObservado();
        public abstract void LimpiarVehEscape();
        public abstract bool EstaOcupadoBucleSalida();
        public abstract bool GetUltimoSentidoEsOpuesto();
        public abstract int GetComitivaVehPendientes();
        public abstract void LimpiarVehObservado();
        public abstract void DesactivarAntena( eCausaDesactivacionAntena causa );
        public abstract void CapturaVideoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor);
        public abstract void CapturaFotoEscape(ref Vehiculo oVehiculo, eCausaVideo sensor);
        public abstract void CapturaFoto(ref Vehiculo oVehiculo, ref eCausaVideo sensor, bool esManual = false );
        public abstract void CapturaVideo(ref Vehiculo oVehiculo, ref eCausaVideo sensor, bool esManual = false) ;
        public abstract void DecideAlmacenar(eAlmacenaMedio causa, ref Vehiculo oVehiculo);
        public abstract void DecideCaptura(eCausaVideo sensor, ulong NumeroVehiculo = 0);

        public abstract void SetVehiculo(Vehiculo[] aVehiculo);
        public abstract bool UltimoSentidoEsOpuesto { get; set; }
        public abstract int ComitivaVehPendientes { get; set; }
        public abstract void AdelantarVehiculo(eMovimiento movimiento);
        public abstract void AdelantarVehiculosModoD();
        public abstract void ProcesarLecturaTag( eEstadoAntena estado, Tag tag, eTipoLecturaTag tipoLectura, TagBD tagManualOnline, Vehiculo oVehOpcional = null);

        public abstract eErrorTag ValidarTagBaseDatos(ref InfoTag oInfoTag, ref Vehiculo oVehiculo, bool bUsarDatosVehiculo, ref TagBD tagBD);
        public abstract void OpCerradaEvento(short origenTecla, ref Vehiculo oVehiculo);
        public abstract void OpAbortadaEvento(ref Vehiculo oVehiculo);
        public abstract void OpAbortadaModoD(bool bAutomatico = false, eVehiculo eVeh = eVehiculo.eVehP1, string nroTag = "" );
        public abstract bool ViaSinVehPagados();
        public abstract bool ViaVacia();
        public abstract void EliminarTagManualD( string sNumero );
        public abstract int LimpiarVehIng();
        public abstract void SetFlagOcupado(int nVeh, bool bOcup, ulong numeroVehiculo);
        public abstract void LimpiarColaVehiculos();

        public abstract void LimpiarTagsLeidos();
        public abstract bool FallaSensoresDAC(int btFiltrarSensores = 0);

        public abstract bool UltimoAnuladoEsIgual(string sPatente);
        public abstract void ActualizarEstadoSensoresEscape();
        public abstract void IniciarTimerApagadoCampanaPantalla(int? tiempoMseg);
        public abstract void ActualizarEstadoSensores(bool joinThread = false);
        public abstract void Dispose();
        public abstract void LoguearColaVehiculos();
        public abstract bool EstaOcupadoLazoSalida();

        public abstract bool EstaOcupadoSeparadorSalida();
        public abstract void LimpiarVeh(eVehiculo eVeh);
        public abstract void GrabarVehiculos();
        public abstract bool AsignandoTagAVehiculo { get; }
        public abstract void RegularizarFilaVehiculos();
        public abstract void AdelantarFilaVehiculosDesde(eVehiculo inicio);
    }
}
