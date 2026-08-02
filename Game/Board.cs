namespace GameProject {
    public enum Mark {
        None,
        X,
        O
    }

    /// <summary>
    /// The game itself. Nine macro cells, each a tic-tac-toe board of its own, indexed row
    /// major from the top left. There's no drawing and no animation in here: the view reads
    /// this every frame and animates whatever it sees change.
    /// </summary>
    public class Board {
        /// The eight ways to own a three by three board.
        static readonly int[][] _lines = [
            [0, 1, 2], [3, 4, 5], [6, 7, 8],
            [0, 3, 6], [1, 4, 7], [2, 5, 8],
            [0, 4, 8], [2, 4, 6],
        ];

        readonly Mark[] _cells = new Mark[81];
        readonly Mark[] _macro = new Mark[9];

        public Mark Turn { get; private set; } = Mark.X;
        public Mark Winner { get; private set; } = Mark.None;
        public bool IsDraw { get; private set; }
        public bool IsOver => Winner != Mark.None || IsDraw;

        /// <summary>The macro the next play is confined to, or null when it can go anywhere.</summary>
        public int? ForcedMacro { get; private set; }

        public int LastMacro { get; private set; } = -1;
        public int LastMicro { get; private set; } = -1;

        public Mark Cell(int macro, int micro) => _cells[macro * 9 + micro];
        public Mark MacroOwner(int macro) => _macro[macro];

        /// <summary>A macro nobody can play in again, either won or full.</summary>
        public bool IsSettled(int macro) {
            if (_macro[macro] != Mark.None) return true;
            for (int i = 0; i < 9; i++) {
                if (_cells[macro * 9 + i] == Mark.None) return false;
            }
            return true;
        }

        public bool IsPlayable(int macro) {
            if (IsOver || IsSettled(macro)) return false;
            return ForcedMacro == null || ForcedMacro.Value == macro;
        }
        public bool IsPlayable(int macro, int micro) =>
            IsPlayable(macro) && _cells[macro * 9 + micro] == Mark.None;

        public bool TryPlay(int macro, int micro) {
            if (!IsPlayable(macro, micro)) return false;

            _cells[macro * 9 + micro] = Turn;
            LastMacro = macro;
            LastMicro = micro;

            if (_macro[macro] == Mark.None) {
                _macro[macro] = WinnerOf(_cells, macro * 9);
            }
            Winner = WinnerOf(_macro, 0);

            // Being sent to a board that's already settled frees the next player to go anywhere.
            ForcedMacro = IsSettled(micro) ? null : micro;
            Turn = Turn == Mark.X ? Mark.O : Mark.X;

            if (Winner == Mark.None) {
                IsDraw = !AnyMoveLeft();
            }
            return true;
        }

        public void Reset() {
            System.Array.Clear(_cells);
            System.Array.Clear(_macro);
            Turn = Mark.X;
            Winner = Mark.None;
            IsDraw = false;
            ForcedMacro = null;
            LastMacro = -1;
            LastMicro = -1;
        }

        bool AnyMoveLeft() {
            for (int m = 0; m < 9; m++) {
                if (!IsSettled(m)) return true;
            }
            return false;
        }

        static Mark WinnerOf(Mark[] slots, int offset) {
            foreach (int[] line in _lines) {
                Mark a = slots[offset + line[0]];
                if (a != Mark.None && a == slots[offset + line[1]] && a == slots[offset + line[2]]) {
                    return a;
                }
            }
            return Mark.None;
        }
    }
}
