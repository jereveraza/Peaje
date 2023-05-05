using System.Windows;
using Entidades.Comunicacion;
using Entidades;
using System.Windows.Input;

namespace ModuloPantallaTeclado.Interfaces
{
    public enum enmSubVentana
    {
        Principal, IngresoSistema, ModoApertura, ModoSupervisor, MenuPrincipal, Foto, Exento,
        Patente, TagOcrManual, FondoCambio, RetiroAnticipado, CrearPagoDiferido,
        CobroDeudas, /*TicketManual,*/ TicketManualComitiva, Observaciones, MonedaExtranjera,
        Recorridos, Venta, MensajesSupervision, VentanaConfirmacion,
        CambioPassword, Factura, Liquidacion, AutorizacionNumeracion, ViaEstacion, Simbolo,
        AutorizacionPasoVale, Versiones, CantEjes, Categorias, FormaPago, EncuestaUsuarios, Vuelto
    }

    public interface ISubVentana
    {
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        FrameworkElement ObtenerControlPrincipal();

        /// <summary>
        /// A este metodo llegan las teclas recibidas desde la pantalla principal
        /// </summary>
        /// <param name="tecla"></param>
        void ProcesarTecla(Key tecla);

        /// <summary>
        /// A este metodo llegan las teclas que se sueltan desde la pantalla principal
        /// </summary>
        /// <param name="tecla"></param>
        void ProcesarTeclaUp(Key tecla);

        /// <summary>
        /// Este metodo recibe el string JSON que llega desde el socket
        /// </summary>
        /// <param name="comandoJson"></param>
        bool RecibirDatosLogica(ComandoLogica comandoJson);

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion);

        void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false);
    }
}
