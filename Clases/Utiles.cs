using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ProyectoOCR;
using System.IO;
using Utiles;
using Entidades.ComunicacionBaseDatos;
using System.Collections.Generic;
using System.Linq;
using Entidades.Comunicacion;
using System.Windows;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Clases
{
    public static class Utiles
    {
        #region Variables y propiedades de clase

        public enum FormatoPatente { Abierto, Cerrado}
        private static string Simbolos { get; set; }
        private static Regex _regexFiltroPatentes;
        private static Regex _regexFiltroPatenteAbierta;
        private static Regex _regexFiltroRUC;
        private static int MeAlejoAbajo;
        private static int MeAlejoArriba;
        private static int MeAlejoIzq;
        private static int MeAlejoDere;
        private static int ImageHeight = 600;
        private static int ImageWidth = 800;
        #endregion

        #region Metodos de Filtrado de formato de patentes
        /// <summary>
        /// Devuelve si el formato de patente ingresada es correcto o no
        /// </summary>
        /// <param name="patente"></param>
        /// <returns></returns>
        public static bool EsPatenteValida(FormatoPatente formato, string patente, bool IngresoObligatorio)
        {
            bool patenteValida = false;

            if (!IngresoObligatorio && patente == string.Empty)
                patenteValida = true;
            else
            {
                if (patente.Length >= 6)
                {
                    //if (_regexFiltroPatentes != null && _regexFiltroPatentes.Match(patente).Success)
                        patenteValida = true;
                }
                //else
                //{
                //    if (_regexFiltroPatenteAbierta != null && _regexFiltroPatenteAbierta.Match(patente).Success)
                //        patenteValida = true;
                //}
            }
            return patenteValida;
        }

        private static string CargarFiltroRegex( string filtro, ref ListadoExpresiones listaTotalRegEx )
        {
            string retRegEx = string.Empty;

            List<ExpresionRegular> listaRegExParticulares = listaTotalRegEx.ListaExpresiones.Where( x => x.Descripcion.Contains( filtro ) ).ToList();

            foreach( ExpresionRegular eR in listaRegExParticulares )
            {
                retRegEx += "(" + eR.Expresion + ")";

                // Solo le agrego | si no es el ultimo elemento
                if( !eR.Equals( listaRegExParticulares.Last() ) )
                    retRegEx += "|";
            }

            return retRegEx;
        }

        /// <summary>
        /// Inicializa el filtro de formato de patentes de acuerdo al app.config
        /// </summary>
        public static void CargarRegex(string sObjeto)
        {
            ListadoExpresiones lExpre = ClassUtiles.ExtraerObjetoJson<ListadoExpresiones>(sObjeto);

            string filtroPatente = CargarFiltroRegex( "Patente Cerrado", ref lExpre );
            string filtroPatenteAbierta = CargarFiltroRegex("Patente Abierto", ref lExpre);
            string filtroRUC = CargarFiltroRegex( "Documento", ref lExpre );

            _regexFiltroPatentes = new Regex( filtroPatente, RegexOptions.IgnoreCase | RegexOptions.Singleline |
                                                             RegexOptions.CultureInvariant | RegexOptions.Compiled );
            _regexFiltroPatenteAbierta = new Regex(filtroPatenteAbierta, RegexOptions.IgnoreCase | RegexOptions.Singleline |
                                                             RegexOptions.CultureInvariant | RegexOptions.Compiled);

            _regexFiltroRUC = new Regex( filtroRUC, RegexOptions.IgnoreCase | RegexOptions.Singleline |
                                                    RegexOptions.CultureInvariant | RegexOptions.Compiled );
            
        }
        #endregion

        #region Cargar Lista de Simbolos
        public static void CargarSimbolos(string sObjeto)
        {
            Simbolos = sObjeto;
        }

        public static string ObtenerSimbolos()
        {
            return Simbolos;
        }
        #endregion

        #region Metodo para validacion de RUC
        public static bool IsValidPartialRUC(string RUC)
        {
            bool rucValido = false;
            
            if( _regexFiltroRUC != null && _regexFiltroRUC.Match( RUC ).Success )
                rucValido = true;

            return rucValido;
        }

        /// <summary>
        /// Valida el RUC ingresado.
        /// </summary>
        /// <param name="ruc" />Número de RUC como string con o sin guiones
        /// <returns>True si el RUC es válido y False si no.</returns>
        public static bool ValidaRucPeru(string ruc)
        {
            bool ret = false;
            int suma = 0, x = 6;
            if (ruc.Length == 11)
            {
                for (int i = 0; i < ruc.Length - 1; i++)
                {
                    if (i == 4)
                        x = 8;
                    var digito = ruc[i] - '0';
                    x--;
                    suma += digito * x;
                }
                int resto = suma % 11;
                resto = 11 - resto;
                if (resto >= 10)
                    resto = resto - 10;
                if (resto == ruc[ruc.Length - 1] - '0')
                    ret = true;
            }
            return ret;
        }

        public static bool IsValidRUC(string strRuc)
        {
            bool RucValido = false;
            int suma = 0, resto = 0, i = 0, impar = 0, par = 0, digito10 = 0, nAux = 0;
            byte digito;

            // Si es un ruc de 13 Caracteres
            if (strRuc.Length == 13)
            {
                //Numero de Provincia
                nAux = Convert.ToInt32(strRuc.Substring(0, 2));
                //Verifico que el numero de provincia este entre 1 y 24 
                if (nAux <= 0 && nAux > 24)
                {
                    return false;
                }

                //El principal o sucursal debe ser 001
                if (strRuc.Substring(10, 3) != "001")
                {
                    return false;
                }

                //El digito
                digito = Convert.ToByte(strRuc.Substring(2, 1));
                //RUC PERSONA NATURAL
                if (digito >= 0 && digito < 6)
                {
                    digito = 0;
                    for (i = 0; i < 9; i += 2)
                    {
                        digito = Convert.ToByte(strRuc.Substring(i, 1));
                        impar = impar + ((digito * 2) > 9 ? digito * 2 - 9 : digito * 2);
                    }
                    for (i = 1; i < 8; i += 2)
                    {
                        digito = Convert.ToByte(strRuc.Substring(i, 1));
                        par = par + digito;
                    }
                    suma = par + impar;
                    resto = suma % 10;
                    if (resto != 0)
                        resto = 10 - resto;
                    if (resto == 10)
                        resto = 0;
                    digito10 = Convert.ToInt32(strRuc.Substring(9, 1));
                    if (resto == digito10)
                        RucValido = true;
                    else
                        RucValido = false;
                }
                // PERSONA JURIDICA O EXTRANJERA
                else if (digito == 9)
                {
                    int[] coef9 = new int[] { 4, 3, 2, 7, 6, 5, 4, 3, 2 };
                    for (i = 0; i < 9; i++)
                    {
                        digito = Convert.ToByte(strRuc.Substring(i, 1));
                        suma = suma + digito * coef9[i];
                    }
                    resto = suma % 11;
                    if (resto != 0)
                        resto = 11 - resto;
                    if (resto == 10)
                        resto = 0;
                    digito10 = Convert.ToInt32(strRuc.Substring(9, 1));
                    if (resto == digito10)
                        RucValido = true;
                    else
                        RucValido = false;
                }
                else if (digito == 6)
                {
                    int[] coef8 = new int[] { 3, 2, 7, 6, 5, 4, 3, 2 };
                    for (i = 0; i < 8; i++)
                    {
                        digito = Convert.ToByte(strRuc.Substring(i, 1));
                        suma = suma + digito * coef8[i];
                    }
                    resto = suma % 11;
                    if (resto != 0)
                        resto = 11 - resto;
                    if (resto == 10)
                        resto = 0;
                    digito10 = Convert.ToByte(strRuc.Substring(8, 1));
                    if (resto == digito10)
                        RucValido = true;
                    else
                        RucValido = false;
                }
                else
                {
                    RucValido = false;
                }
            }
            // Si es un ruc de 10 Caracteres
            if (strRuc.Length == 10)
            {
                digito = 0;
                for (i = 0; i < 9; i += 2)
                {
                    digito = Convert.ToByte(strRuc.Substring(i, 1));
                    impar = impar + ((digito * 2) > 9 ? digito * 2 - 9 : digito * 2);
                }
                for (i = 1; i < 8; i += 2)
                {
                    digito = Convert.ToByte(strRuc.Substring(i, 1));
                    par = par + digito;
                }
                suma = par + impar;
                resto = suma % 10;
                if (resto != 0)
                    resto = 10 - resto;
                if (resto == 10)
                    resto = 0;
                digito10 = Convert.ToInt32(strRuc.Substring(9, 1));
                if (resto == digito10)
                    RucValido = true;
                else
                    RucValido = false;
            }
            return RucValido;
        }
        #endregion


        #region Metodo que carga la configuracion del zoom del app.config
        public static void CargarConfigCortarPatente()
        {
            int nAux;
            bool bOk;

            bOk = int.TryParse(ConfigurationManager.AppSettings["ZOOM_BOTTOM_MARGIN"].ToString(), out nAux);
            MeAlejoAbajo = bOk ? nAux : 0;

            bOk = int.TryParse(ConfigurationManager.AppSettings["ZOOM_TOP_MARGIN"].ToString(), out nAux);
            MeAlejoArriba = bOk ? nAux : 0;

            bOk = int.TryParse(ConfigurationManager.AppSettings["ZOOM_LEFT_MARGIN"].ToString(), out nAux);
            MeAlejoIzq = bOk ? nAux : 0;

            bOk = int.TryParse(ConfigurationManager.AppSettings["ZOOM_RIGTH_MARGIN"].ToString(), out nAux);
            MeAlejoDere = bOk ? nAux : 0;
        }
        #endregion


        public static Point FrameworkElementPointToScreenPoint(FrameworkElement textBox)
        {
            // Get absolute location on screen of upper left corner of the UserControl
            Point locationFromScreen = textBox.PointToScreen(new Point(0, 0));
            // Transform screen point to WPF device independent point
            PresentationSource source = PresentationSource.FromVisual(Application.Current.MainWindow);
            Point targetPoints = source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);

            // Get Focus
            Point _posicion = new Point();
            _posicion.X = targetPoints.X + (textBox.ActualWidth / 2.0);
            _posicion.Y = targetPoints.Y + (textBox.ActualHeight);

            return _posicion;
        }

        #region Metodo que carga una foto en un control rectangulo
        /// <summary>
        /// Carga una foto en un control rectangulo del tamaño especificado y lo retorna
        /// </summary>
        /// <param name="strImagePath"></param>
        /// <param name="ancho"></param>
        /// <param name="alto"></param>
        /// <param name="imgRecortada"></param>
        /// <returns></returns>
        public static Rectangle CargarFotoRectangulo(string strImagePath, double ancho, double alto, bool imgRecortada, NLog.Logger logger)
        {
            Rectangle _rectangle = new Rectangle();
            ImageBrush _uniformBrush = new ImageBrush();
            BitmapImage _image = new BitmapImage();
            _rectangle.Width = ancho;
            _rectangle.Height = alto;

            try
            {
                if (imgRecortada)
                {
                    using (var stream = File.OpenRead(strImagePath))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.Default);
                        ImageHeight = decoder.Frames[0].PixelHeight;
                        ImageWidth = decoder.Frames[0].PixelWidth;
                    }
                    byte[] imageArray = File.ReadAllBytes(strImagePath);
                    string imagen = Convert.ToBase64String(imageArray);
                    bool bResutado = false;
                    ManejoImagen CropImage = new ManejoImagen();
                    //Vuelvo a cargar la configuracion por si cambio
                    CargarConfigCortarPatente();
                    imagen = CropImage.ObtenerImagenMatricula(imagen, MeAlejoIzq, MeAlejoArriba, ImageWidth - MeAlejoDere, ImageHeight - MeAlejoAbajo, out bResutado);

                    byte[] unicodeBytes = Convert.FromBase64String(imagen);

                    using (MemoryStream ms = new MemoryStream(unicodeBytes, 0, unicodeBytes.Length))
                    {
                        _image.BeginInit();
                        _image.StreamSource = ms;
                        _image.CacheOption = BitmapCacheOption.OnLoad;
                        _image.StreamSource = ms;
                        _image.EndInit();
                        _uniformBrush.ImageSource = _image;
                        _uniformBrush.Stretch = Stretch.Uniform;
                        // Freeze the brush (make it unmodifiable) for performance benefits.
                        _uniformBrush.Freeze();
                        _rectangle.Fill = _uniformBrush;
                    }
                }
                else
                {
                    using (var stream = File.OpenRead(strImagePath))
                    {
                        _image.BeginInit();
                        _image.CacheOption = BitmapCacheOption.OnLoad;
                        _image.StreamSource = stream;
                        _image.EndInit();
                        _uniformBrush.ImageSource = _image;
                        _uniformBrush.Stretch = Stretch.Uniform;
                        // Freeze the brush (make it unmodifiable) for performance benefits.
                        _uniformBrush.Freeze();
                        _rectangle.Fill = _uniformBrush;
                    }
                }

            }
            catch (Exception e)
            {
                logger.Warn("CargarFotoRectangulo: Error al cargar la foto.");
            }
            return _rectangle;
        }
        #endregion

        public static void TraducirControles<T>(DependencyObject depObj) where T : DependencyObject
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                foreach (T tb in FindVisualChildren<T>(depObj))
                {
                    if (tb is TextBlock)
                    {
                        TextBlock t = (TextBlock)Convert.ChangeType(tb, typeof(TextBlock));
                        if (!string.IsNullOrEmpty(t.Text) && !string.IsNullOrEmpty(t.Name))
                        {
                            t.Text = Traduccion.Traducir(t.Text);
                        }
                    }
                }
            }));
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

    }
}
