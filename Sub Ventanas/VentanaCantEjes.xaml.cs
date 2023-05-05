using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Collections.Generic;
using Entidades.ComunicacionFoto;
using Newtonsoft.Json;
using System.IO;
using System.Timers;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using System.Diagnostics;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaPatente.xaml
    /// </summary>
    public partial class VentanaCantEjes : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private Causa _causa = null;
        private AgregarSimbolo _ventanaSimbolo;
        private Point _posicionSubV;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        private const byte _maximoCaracteresPatente = 8;
        #endregion

        #region Constructor de la clase
        public VentanaCantEjes(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                _causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);

                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    lblEjes.Text = "Ingrese cantidad de ejes:";
                    txtEjes.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }));

                Vehiculo vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);
                _pantalla.TecladoVisible();
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
            FrameworkElement control = (FrameworkElement)borderVentanaCantEjes.Child;
            borderVentanaCantEjes.Child = null;
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
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre
                        || causa.Codigo == eCausas.Salidavehiculo)
                    {
                        //Logica indica que se debe cerrar la ventana
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaPatente:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al recibir una Respuesta de logica.");
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
            catch
            {

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

            List<DatoVia> listaDV = new List<DatoVia>();
            if (_ventanaSimbolo != null && _ventanaSimbolo.TieneFoco)
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    _ventanaSimbolo.TieneFoco = false;
                    _ventanaSimbolo.Close();
                }
                else
                    _ventanaSimbolo.ProcesarTecla(tecla);
            }
            else
            {
                if (Teclado.IsConfirmationKey(tecla))
                {
                    if (txtEjes.Text.Length >= 0)
                    {
                        //Compruebo si el formato de la patente es correcto
                        Vehiculo vehiculo = new Vehiculo();
                        switch (txtEjes.Text)
                        {
                            case "10":
                                vehiculo.Categoria = 10;
                                break;
                            case "11":
                                vehiculo.Categoria = 11;
                                break;
                            case "12":
                                vehiculo.Categoria = 12;
                                break;
                            case "13":
                                vehiculo.Categoria = 13;
                                break;
                            case "14":
                                vehiculo.Categoria = 14;
                                break;
                            case "15":
                                vehiculo.Categoria = 15;
                                break;
                            case "16":
                                vehiculo.Categoria = 16;
                                break;
                            case "17":
                                vehiculo.Categoria = 17;
                                break;
                            case "18":
                                vehiculo.Categoria = 18;
                                break;
                            case "19":
                                vehiculo.Categoria = 19;
                                break;
                            case "20":
                                vehiculo.Categoria = 20;
                                break;
                            default:
                                _pantalla.MensajeDescripcion("La cantidad de Ejes no corresponde a una categoria");
                                break;

                        }

                        Utiles.ClassUtiles.InsertarDatoVia( vehiculo, ref listaDV );
                        Utiles.ClassUtiles.InsertarDatoVia( _causa, ref listaDV );

                        EnviarDatosALogica( enmStatus.Ok, enmAccion.T_CATEGORIAESPECIAL, JsonConvert.SerializeObject( listaDV, jsonSerializerSettings ) );
                        _pantalla.TecladoOculto();

                    }
                    else
                        _pantalla.MensajeDescripcion("Formato incorrecto");
                }              
                else if (Teclado.IsEscapeKey(tecla))
                {                 
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_CATEGORIAESPECIAL, string.Empty);
                    _pantalla.TecladoOculto();
                }
                else if (Teclado.IsBackspaceKey(tecla))
                {
                    if (txtEjes.Text.Length > 0)
                        txtEjes.Text = txtEjes.Text.Remove(txtEjes.Text.Length - 1);
                }
                else
                {
                    if (txtEjes.Text.Length < _maximoCaracteresPatente)
                    {
                        if (Teclado.IsLowerCaseOrNumberKey(tecla))
                            txtEjes.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                }
            }
        }
        #endregion

        private void ProcesarSimbolo(Opcion item)
        {
            _ventanaSimbolo.Close();
            if (txtEjes.Text.Length < 4)
            {
                string sNuevoSimb = item == null ? string.Empty : item?.Descripcion;
                txtEjes.Text = txtEjes.Text + sNuevoSimb;
            }
        }


        private void EJES_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.TecladoVisible();
        }


        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Escape);
        }
    }
}
