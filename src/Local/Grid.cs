using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// A grade da tela dividida, em coordenadas de câmera (0..1, y para cima). As linhas são as da grade do Hub
    /// (1; 2 lado a lado; 2x2; 3x2; 3x3; 4x3), mas os jogadores se dividem por igual entre elas e cada linha ocupa
    /// a largura toda: nunca sobra canto vazio (10 = 4+3+3; 7 = 3+2+2; 3 = 2+1).
    /// </summary>
    static class Grid
    {
        public static (int cols, int rows) Shape(int n)
        {
            if (n <= 1) return (1, 1);
            if (n == 2) return (2, 1);
            if (n <= 4) return (2, 2);
            if (n <= 6) return (3, 2);
            if (n <= 9) return (3, 3);
            return (4, 3);
        }

        /// <summary>Quantos jogadores ficam na linha (de cima para baixo): as primeiras levam a sobra.</summary>
        public static int InRow(int n, int row)
        {
            int rows = Shape(n).rows;
            return n / rows + (row < n % rows ? 1 : 0);
        }

        public static Rect Part(int n, int i)
        {
            int rows = Shape(n).rows;
            float h = 1f / rows;
            for (int row = 0, first = 0; row < rows; first += InRow(n, row), row++)
            {
                int inRow = InRow(n, row);
                if (i >= first + inRow) continue;
                float w = 1f / inRow;
                // a grade conta as linhas de cima para baixo; a câmera conta y de baixo para cima
                return new Rect((i - first) * w, 1f - (row + 1) * h, w, h);
            }
            return new Rect(0f, 0f, 1f, 1f);
        }
    }
}
