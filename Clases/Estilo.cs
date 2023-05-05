using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace ModuloPantallaTeclado.Clases
{
    public enum ResourceList
    {
        ActionButtonStyle,
        ActionButtonStyleHighlighted,
        ActionButtonCashStyle,
        BorderStyle,
        BorderStyleHighlighted,
        BorderIconStyle,
        BorderIconStyleHighlighted,
        BorderIconStyleError,
        BorderIconStyleWarning,
        PasswordStyle,
        PasswordStyleHighlighted,
        TextBoxStyle,
        TextBoxStyleHighlighted,
        TextBoxLiquidacionStyle,
        BlockColor,
        BackgroundColor,
        LabelFontColor,
        ValueFontColor,
        LabelFontColor2,
        TextBoxListHighlightedStyle,
        TextBoxListStyle,
        ListBoxItemStyle,
        ListBoxTextStyle,
        ListBoxTextStyleInverted,

        EstadoViaAbiertaBackgroundColor,
        EstadoViaAbiertaBlockColor,
        EstadoViaAbiertaLabelFontColor,
        EstadoViaAbiertaValueFontColor,
        EstadoViaAbiertaLabelFontColor2,

        EstadoViaCerradaBackgroundColor,
        EstadoViaCerradaBlockColor,
        EstadoViaCerradaLabelFontColor,
        EstadoViaCerradaValueFontColor,
        EstadoViaCerradaLabelFontColor2,

        EstadoViaMantenimientoBackgroundColor,
        EstadoViaMantenimientoBlockColor,
        EstadoViaMantenimientoLabelFontColor,
        EstadoViaMantenimientoValueFontColor,
        EstadoViaMantenimientoLabelFontColor2,

        EstadoViaQuiebreBackgroundColor,
        EstadoViaQuiebreBlockColor,
        EstadoViaQuiebreLabelFontColor,
        EstadoViaQuiebreValueFontColor,
        EstadoViaQuiebreLabelFontColor2,
    }
    
    public static class Estilo
    {
        public static T FindResource<T>(ResourceList resource)
        {
            return (T)Application.Current.FindResource(Enum.GetName(typeof(ResourceList), resource));
        }

        public static void UpdateResource(ResourceList resource, object value)
        {
            Application.Current.Resources[Enum.GetName(typeof(ResourceList), resource)] = value;
        }
    }
}
