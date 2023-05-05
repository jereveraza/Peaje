using Entidades;
using Entidades.Comunicacion;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ModuloPantallaTeclado.Clases
{
    public partial class Scroll
    {
        private static ScrollViewer FindViewer(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>(new[] { root });

            do
            {
                var item = queue.Dequeue();
                if (item is ScrollViewer) { return (ScrollViewer)item; }
                var count = VisualTreeHelper.GetChildrenCount(item);
                for (var i = 0; i < count; i++) { queue.Enqueue(VisualTreeHelper.GetChild(item, i)); }
            } while (queue.Count > 0);

            return null;
        }

        public static void ToBottom(ListBox listBox)
        {
            var scrollViewer = FindViewer(listBox);

            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += (o, args) =>
                {
                    if (args.ExtentHeightChange > 0) { scrollViewer.ScrollToBottom(); }
                };
            }
        }
    }

    public class MensajesPantalla
    {
        private string _msgLinea1, _msgLinea2, _msgLinea3, _msgSupervision;

        public string MensajeLinea1 { get { return _msgLinea1; } }
        public string MensajeLinea2 { get { return _msgLinea2; } }
        public string MensajeLinea3 { get { return _msgLinea3; } }
        public string MensajeSupervision { get { return _msgSupervision; } }

        public void LimpiarMensajesPantalla()
        {
            _msgLinea1 = string.Empty;
            _msgLinea2 = string.Empty;
            _msgLinea3 = string.Empty;
            _msgSupervision = string.Empty;
        }

        public void ActualizarMensaje(Mensajes nuevoMensaje)
        {
            switch (nuevoMensaje.TipoMensaje)
            {
                case enmTipoMensaje.Linea1:
                    {
                        _msgLinea1 = nuevoMensaje.Mensaje;
                        break;
                    }
                case enmTipoMensaje.Linea2:
                    {
                        _msgLinea2 = nuevoMensaje.Mensaje;
                        break;
                    }
                case enmTipoMensaje.Linea3:
                    {
                        _msgLinea3 = nuevoMensaje.Mensaje;
                        break;
                    }
                //case enmTipoMensaje.MsgSupervision:
                //    {
                //        _msgSupervision = nuevoMensaje.Mensaje;
                //        break;
                //    }
            }
        }
    }
}
