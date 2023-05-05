using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows.Media;
using Entidades.Logica;
using Entidades.Comunicacion;
using Entidades;
using Entidades.ComunicacionBaseDatos;
using ModuloPantallaTeclado.Clases;
using System;

namespace ModuloPantallaTeclado.Interfaces
{
    public enum enmModeloVia { MANUAL, AVI, DINAMICA }

    public enum enmTipoImagen
    {
        categoria0, categoria1, categoria2, categoria3, categoria4, categoria5, categoria6, categoria7, categoria8, categoria9,
        categoria10, categoria11, categoria12, categoria13, categoria14, categoria15, categoria16, categoria17, categoria18, categoria19,
        categoria20, RedNo, RedOk, RedError, RedWarning, ImpresoraOk, ImpresoraError, ImpresoraNo, ImpresoraWarning, ImpresoraCierreZ,
        ImpresoraPocoPapel, BarreraNo, MsgComando, MsgEnviado, MsgRecibido,
        BarreraArriba, BarreraAbajo, BarreraError, AntenaOk, AntenaError, AntenaNo, AntenaWarning, TChipOk, TChipError, TChipNo, TChipReading,
        TChipWarning, SemPasoNo, SemPasoRojo, SemPasoVerde, SemMarquesinaRojo, SemMarquesinaVerde, SeparadorNo, SeparadorLibre, SeparadorOcupado,
        SeparadorError, SeparadorWarning, AlarmaOk, AlarmaActiva, AlarmaNo, LogoCliente, LogoTelectronica,
        DisplayOk, DisplayError, DisplayWarning, DisplayNo, AlturaBajo, AlturaError, AlturaWarning, AlturaAlto, AlturaNo,
        LazoSalidaLibre, LazoSalidaWarning, LazoSalidaOcupado, LazoSalidaNo, LazoSalidaError, PinPadNo, PinPadWarning, PinPadOk, PinPadError
    }

    public interface IPantalla
    {
        /// <summary>
        /// Propiedad que almacena el parte recibido desde logica
        /// </summary>
        Parte ParteRecibido { set; get; }

        /// <summary>
        /// Propiedad que almacena el turno recibido desde logica
        /// </summary>
        Turno TurnoRecibido { set; get; }

        /// <summary>
        /// Propiedad que almacena informacion de la via recibida desde logica
        /// </summary>
        InformacionVia InformacionViaRecibida { set; get; }

        /// <summary>
        /// Propiedad que almacena el vehiculo recibido desde logica
        /// </summary>
        Vehiculo VehiculoRecibido { set; get; }

        /// <summary>
        /// Propiedad para pasar un parametro desde el padre
        /// </summary>
        string ParametroAuxiliar { set; get; }

        /// <summary>
        /// Propiedad que recibe la configuracon de via necesaria desde logica
        /// </summary>
        ConfigVia ConfigViaRecibida { set; get; }
        
        /// <summary>
        /// Este metodo se llama cuando se desconecta un cliente.
        /// </summary>
        void CambioEstadoConexion();

        /// <summary>
        /// Se encarga de cambiar a la ventana indicada (IngresoSistema, MenuCierre, etc.). 
        /// <param name="subVentana"></param>
        /// </summary>
        void CargarSubVentana(enmSubVentana subVentana);

        /// <summary>
        /// Se encarga de recibir la tecla pulsada desde la vantana principal. 
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// </summary>         
        void OnTecla(object sender, KeyEventArgs e);

        void OnTeclaUp(object sender, KeyEventArgs e);

        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        FrameworkElement ObtenerControlPrincipal();

        /// <summary>
        /// Este metodo recibe el string JSON que llega desde el socket
        /// </summary>
        /// <param name="comandoJson"></param>
        void RecibirDatosLogica(ComandoLogica comandoJson);

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion);

        /// <summary>
        /// Metodo que muestra un mensaje en el cuadro de descripcion
        /// </summary>
        /// <param name="mensaje"></param>
        void MensajeDescripcion(string mensaje, bool traducir = true, int lineaMensaje = 1);

        void TecladoVisible();
        void TecladoOculto();

        /// <summary>
        /// Carga las imagenes como brushes en un diccionario
        /// </summary>
        /// <param name="carpetaRecursos"></param>
        /// <returns></returns>
        IDictionary<enmTipoImagen, ImageBrush> CargarImagenesRecursos(string carpetaRecursos);

        /// <summary>
        /// Recibe la tecla que fue presionada y llama a la función correspondiente a ser ejecutada.
        /// <param name="tecla"></param>
        /// </summary>
        void AnalizaTecla(Key tecla);
        void AnalizaToque(Key tecla);

        event Action<TextCompositionEventArgs> LectorCodigoBarrasEvent;
        void OnLectorCodigoBarras(TextCompositionEventArgs e);

        void Dispose();
    }
}
