using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionEventos;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Utiles;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaVersiones.xaml
    /// </summary>
    public partial class VentanaVersiones : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuPrincipal _enmMenuPrincipal;
        private ListBoxItem _ItemSeleccionado;
        private int _cantidadOpcionesMenu;
        private bool _bPrimerDigito = false;
        private int _OpcionElegida;
        private string _colorItemSeleccionado;
        private ListadoOpciones _listaOpcionesMenu = new ListadoOpciones();
        private Causa _causa;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private object objParameter = null;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Constructor de la clase
        public VentanaVersiones(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(gridVentanaVersiones);
            try
            {
                //Cargo las opciones del listbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    List<EventoVersion> listaVersiones = ClassUtiles.ExtraerObjetoJson<List<EventoVersion>>(_pantalla.ParametroAuxiliar);
                    _causa = ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);

                    lblTituloMenu.Dispatcher.Invoke((Action)(() => lblTituloMenu.Text = Traduccion.Traducir(_causa.Descripcion)));

                    foreach (var opt in listaVersiones)
                    {
                        listBoxMenu.Items.Add(string.Format("{0} - {1} : {2}", opt.GetVersionString,opt.FechaModif.ToString("dd/MM/yyyy"), opt.TipPro));
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaVersiones:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaVersiones:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = Traduccion.Traducir(BtnAceptarSiguiente ? "Siguiente" : "Confirmar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
                btnCancelar.Content = Traduccion.Traducir("Volver") + " [" + Teclado.GetEtiquetaTecla(TeclaCancelacion) + "]";
            }));
        }

        #region Metodo para obtener el control desde el padre
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderVentanaVersiones.Child;
            borderVentanaVersiones.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodos de comunicacion con el modulo de logica
        /// <summary>
        /// Este metodo recibe el string JSON que llega desde el socket
        /// </summary>
        /// <param name="comandoJson"></param>
        public bool RecibirDatosLogica(ComandoLogica comandoJson)
        {
            bool bRet = false;
            try
            {
                if (comandoJson.CodigoStatus == enmStatus.Ok
                    && comandoJson.Accion == enmAccion.ESTADO)
                {
                    bool cierroVentana = false;
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (_causa.Codigo == eCausas.AperturaTurno)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.CausaCierre)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.TipoExento)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre
                            || causa.Codigo == eCausas.Salidavehiculo)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.CausaSimulacion)
                    {
                        cierroVentana = true;
                    }
                    else if (_causa.Codigo == eCausas.CausaCancelacion)
                    {
                        cierroVentana = true;
                    }
                    else if (_causa.Codigo == eCausas.Observacion)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.Retiro)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.VentaRecarga)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre
                            || causa.Codigo == eCausas.Salidavehiculo)
                        {
                            cierroVentana = true;
                        }
                    }

                    if (cierroVentana)
                    {
                        //Logica indica que se debe cerrar la ventana
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    _enmMenuPrincipal = enmMenuPrincipal.IngresoOpcion;
                }
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaVersiones:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaVersiones:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            return bRet;
        }

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        public void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion)
        {
            try
            {
                _pantalla.EnviarDatosALogica(status, Accion, Operacion);
            }
            catch (Exception ex)
            {
                _logger.Debug("MenuPrincipal:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
        public void ProcesarTeclaUp(Key tecla)
        {
            if (Teclado.IsEscapeKey(tecla))
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    btnCancelar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                }));
            }
        }

        /// <summary>
        /// A este metodo llegan las teclas recibidas desde la pantalla principal
        /// </summary>
        /// <param name="tecla"></param>
        public void ProcesarTecla(Key tecla)
        {
            //Aplico el estilo al presionar
            if (Teclado.IsEscapeKey(tecla))
            {
                btnCancelar.Dispatcher.Invoke((Action)(() =>
                {
                    btnCancelar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }
            
            if (Teclado.IsEscapeKey(tecla) || Teclado.IsConfirmationKey(tecla))
            {
                _pantalla.MensajeDescripcion(string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
            }
        }
        #endregion

    }
}
