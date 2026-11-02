using PartyAnywhere;

namespace PeakCoop.Input
{
    /// <summary>
    /// O nome de cada ação nos botões do celular, igual ao mapa do <see cref="SeatInput"/> e ao controle da ficha
    /// (`controls`). Nomes curtos: no celular o botão é pequeno e precisa ser lido de relance. O Hub guarda por vaga
    /// enquanto o jogo está conectado.
    /// </summary>
    static class PhoneLabels
    {
        static readonly (Buttons button, string label)[] labels =
        {
            (Buttons.LabelStickL, "Andar"),
            (Buttons.LabelStickR, "Olhar"),
            (Buttons.A, "Pular"),
            (Buttons.B, "Agachar"),
            (Buttons.X, "Usar"),
            (Buttons.Y, "Soltar"),
            (Buttons.LabelRT, "Agarrar"),
            (Buttons.LabelLT, "Item"),
            (Buttons.LB, "Correr"),
            (Buttons.RB, "Marcar"),
            (Buttons.RS, "Emotes"),
            (Buttons.Left, "Itens"), // os direcionais dividem um nome só
            (Buttons.Start, "Pausa"),
        };

        public static void Send(Client hub, int seat)
        {
            foreach (var (button, label) in labels) hub.SetLabel(seat, button, label);
        }
    }
}
