using Newtonsoft.Json;

namespace ModuloPantallaTeclado.Entidades
{
    class PagoDeuda
    {
        private string m_sPatente, m_sNumero;
        private long m_lMonto;

        [JsonProperty("Numero")]
        public string Numero { get { return m_sNumero; } set { m_sNumero = value; } }
        [JsonProperty("Patente")]
        public string Patente { get { return m_sPatente; } set { m_sPatente = value; } } 
        [JsonProperty("Monto")]
        public long Monto { get { return m_lMonto; } set { m_lMonto = value; } }

        public PagoDeuda()
        {

        }

        public PagoDeuda(string patente, string numero, long monto)
        {
            m_lMonto = monto;
            m_sPatente = patente;
            m_sNumero = numero;
        }
    }
}
