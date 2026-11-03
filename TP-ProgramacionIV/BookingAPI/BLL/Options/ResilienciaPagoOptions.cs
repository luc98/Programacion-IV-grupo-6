namespace BookingAPI.BLL.Options
{
    // Valores de la política de resiliencia de la llamada a Payment (sección "ResilienciaPago").
    // Los defaults son los acordados. Se pueden cambiar por configuración, por ejemplo para
    // acortar el corte del circuito en una demo (ResilienciaPago__CorteSegundos=5).
    public class ResilienciaPagoOptions
    {
        public const string Seccion = "ResilienciaPago";

        // Tope de todo el cobro, reintentos incluidos. Queda muy por debajo del lock de 60 s del
        // mensaje de Service Bus, así el cobro siempre termina antes de que el bus lo reentregue.
        public double TimeoutTotalSegundos { get; set; } = 10;

        // Tope de cada intento. Si Payment no contesta en este tiempo, el intento se cancela.
        public double TimeoutPorIntentoSegundos { get; set; } = 2;

        public int Reintentos { get; set; } = 2;
        public double DemoraInicialMs { get; set; } = 500;
        public double DemoraMaximaMs { get; set; } = 2000;

        // Circuit breaker: se abre si fallan al menos la mitad de las llamadas de los últimos
        // 30 s, con un mínimo de 3 llamadas. Abierto, rechaza sin llamar durante 15 s.
        public double ProporcionDeFallas { get; set; } = 0.5;
        public int MinimoDeLlamadas { get; set; } = 3;
        public double VentanaSegundos { get; set; } = 30;
        public double CorteSegundos { get; set; } = 15;
    }
}
