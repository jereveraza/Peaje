using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace ModuloPantallaTeclado.Entidades
{
    public class Deuda
    {
        private string m_sTipo,m_sEstacion,m_sCategoria,m_sNumero;
        private long m_iMonto;
        private DateTime m_dtFechaHora;
        
        public string Numero { get { return m_sNumero; } set { m_sNumero = value; } }
        public string Tipo { get { return m_sTipo; } set { m_sTipo = value; } }
        public string Estacion { get { return m_sEstacion; } set { m_sEstacion = value; } }
        public string Categoria { get { return m_sCategoria; } set { m_sCategoria = value; } }
        public DateTime FechaHora { get { return m_dtFechaHora; } set { m_dtFechaHora = value; } }
        public long Monto { get { return m_iMonto; } set { m_iMonto = value; } }

        public Deuda()
        {

        }

        public Deuda(string numero,string tipo,string estacion,string categoria,long monto,DateTime fechahora)
        {
            m_sNumero = numero;
            m_sTipo = tipo;
            m_sEstacion = estacion;
            m_sCategoria = categoria;
            m_iMonto = monto;
            m_dtFechaHora = fechahora;
        }
    }

    public class ListaDeuda
    {
        private List<Deuda> m_lListaDeudas = new List<Deuda>();

        [JsonProperty("ListaDeudas")]
        public List<Deuda> ListaDeudas { get { return m_lListaDeudas; } set { m_lListaDeudas = value; } }

        public ListaDeuda()
        {

        }
    }
}
