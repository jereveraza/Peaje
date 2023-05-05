using Newtonsoft.Json;
using System.Collections.Generic;

namespace ModuloPantallaTeclado.Entidades
{
    public class Moneda
    {
        private string m_sNombreMoneda, m_sSimbolo;
        private int m_nCodMoneda, m_iCodOrden;
        private ulong m_dCotizacion;

        public int Orden { get { return m_iCodOrden; } set { m_iCodOrden = value; } }
        public int Codigo { get { return m_nCodMoneda; } set { m_nCodMoneda = value; } }
        public string Nombre { get { return m_sNombreMoneda; } set { m_sNombreMoneda = value; } }
        public string Simbolo { get { return m_sSimbolo; } set { m_sSimbolo = value; } }
        public ulong Cotizacion { get { return m_dCotizacion; } set { m_dCotizacion = value; } }

        //Hago override de Equals y GetHashCode
        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            Moneda objAsPart = obj as Moneda;
            if (objAsPart == null) return false;
            else return Equals(objAsPart);
        }

        public bool Equals(Moneda other)
        {
            if (other == null) return false;
            return (Codigo.Equals(other.Codigo));
        }

        public override int GetHashCode()
        {
            return Codigo;
        }

        public Moneda()
        {

        }

        public Moneda(int orden, int codigo, string nombre, string simbolo, ulong cotizacion)
        {
            m_sNombreMoneda = nombre;
            m_sSimbolo = simbolo;
            m_nCodMoneda = codigo;
            m_dCotizacion = cotizacion;
            m_iCodOrden = orden;
        }
    }

    public class ListaMoneda
    {
        private List<Moneda> m_lListaMonedas = new List<Moneda>();

        [JsonProperty("ListaMonedas")]
        public List<Moneda> ListaMonedas { get { return m_lListaMonedas; } set { m_lListaMonedas = value; } }

        public ListaMoneda()
        {

        }
    }
}
