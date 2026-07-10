namespace SpellyZombie
{
    // The demo wave director that lived here was superseded by Game/RoundDirector
    // (real rounds, intermissions, wipes, ink economy). Only the wallet remains.

    /// Demo score sink — becomes a per-player wallet when multiplayer lands.
    public static class Wallet
    {
        public static int Riches;
    }
}
