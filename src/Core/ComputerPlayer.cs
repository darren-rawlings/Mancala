using System;
using System.Collections.Generic;
using Renegade.Core;

namespace Mancala.Core;

/// <summary>
/// The computer opponent: alpha-beta search on a bare 14-hole array (no history, no allocations
/// in the inner loop, so it runs fast on a phone). Levels differ in depth, and Easy sometimes
/// plays a random move. Extra turns are searched as the same player moving again, without
/// using up depth, so chains of free moves are found.
/// </summary>
public sealed class ComputerPlayer
{
	private const int Win = 10_000;

	private readonly Random _random;

	public Difficulty Difficulty { get; }

	public ComputerPlayer(Difficulty difficulty, int? seed = null)
	{
		Difficulty = difficulty;
		_random = seed is { } s ? new Random(s) : new Random();
	}

	private int Depth => Difficulty switch
	{
		Difficulty.Easy => 1,
		Difficulty.Medium => 4,
		_ => 9,
	};

	public int ChooseMove(Kalah game)
	{
		var moves = new List<int>(game.LegalMoves());
		if (moves.Count == 0)
			throw new InvalidOperationException("No legal moves.");
		if (moves.Count == 1)
			return moves[0];
		// Easy is beatable: a third of its moves are random.
		if (Difficulty == Difficulty.Easy && _random.NextDouble() < 0.33)
			return moves[_random.Next(moves.Count)];

		int me = (int)game.Turn;
		var root = game.CopyHoles();
		int best = int.MinValue;
		var bestMoves = new List<int>();
		Span<int> order = stackalloc int[Kalah.PitsPerSide];
		int count = Order(root, me, order);
		for (int i = 0; i < count; i++)
		{
			var next = (int[])root.Clone();
			int mover = Sow(next, order[i], me, out bool over, out bool extra);
			int score = Search(next, mover, Depth - (extra ? 0 : 1), -Win * 2, Win * 2, me, over);
			if (score > best)
			{
				best = score;
				bestMoves.Clear();
			}
			if (score == best)
				bestMoves.Add(order[i]);
		}
		// Ties are broken at random, so the computer doesn't play the same game every time.
		return bestMoves[_random.Next(bestMoves.Count)];
	}

	private static int Search(int[] holes, int turn, int depth, int alpha, int beta, int me, bool over)
	{
		if (over)
			return Math.Sign(Margin(holes, me)) * Win + Margin(holes, me);
		if (depth <= 0)
			return Evaluate(holes, me);

		Span<int> order = stackalloc int[Kalah.PitsPerSide];
		int count = Order(holes, turn, order);
		var next = new int[Kalah.Holes];
		bool maximising = turn == me;
		int value = maximising ? -Win * 2 : Win * 2;
		for (int i = 0; i < count; i++)
		{
			Array.Copy(holes, next, Kalah.Holes);
			int mover = Sow(next, order[i], turn, out bool done, out bool extra);
			int score = Search(next, mover, extra ? depth : depth - 1, alpha, beta, me, done);
			if (maximising)
			{
				value = Math.Max(value, score);
				alpha = Math.Max(alpha, value);
			}
			else
			{
				value = Math.Min(value, score);
				beta = Math.Min(beta, value);
			}
			if (alpha >= beta)
				break;
		}
		return value;
	}

	/// <summary>The same rules as <see cref="Kalah.Sow"/>, in place on a bare array. Returns who moves next.</summary>
	private static int Sow(int[] holes, int pit, int turn, out bool over, out bool extra)
	{
		int store = turn == 0 ? Kalah.StoreOne : Kalah.StoreTwo;
		int skip = turn == 0 ? Kalah.StoreTwo : Kalah.StoreOne;
		int stones = holes[pit];
		holes[pit] = 0;
		int hole = pit;
		while (stones-- > 0)
		{
			hole = (hole + 1) % Kalah.Holes;
			if (hole == skip)
				hole = (hole + 1) % Kalah.Holes;
			holes[hole]++;
		}
		extra = hole == store;
		bool ownPit = turn == 0 ? hole < Kalah.StoreOne : hole > Kalah.StoreOne && hole < Kalah.StoreTwo;
		if (!extra && ownPit && holes[hole] == 1 && holes[12 - hole] > 0)
		{
			holes[store] += holes[12 - hole] + 1;
			holes[12 - hole] = 0;
			holes[hole] = 0;
		}

		int one = 0, two = 0;
		for (int p = 0; p < Kalah.PitsPerSide; p++)
		{
			one += holes[p];
			two += holes[7 + p];
		}
		over = one == 0 || two == 0;
		if (over)
		{
			for (int p = 0; p < Kalah.PitsPerSide; p++)
			{
				holes[p] = 0;
				holes[7 + p] = 0;
			}
			holes[Kalah.StoreOne] += one;
			holes[Kalah.StoreTwo] += two;
		}
		return extra ? turn : 1 - turn;
	}

	private static int Margin(int[] holes, int me) =>
		me == 0 ? holes[Kalah.StoreOne] - holes[Kalah.StoreTwo] : holes[Kalah.StoreTwo] - holes[Kalah.StoreOne];

	/// <summary>Store difference, plus a little for stones still on your side (they'll likely be yours).</summary>
	private static int Evaluate(int[] holes, int me)
	{
		int one = 0, two = 0;
		for (int p = 0; p < Kalah.PitsPerSide; p++)
		{
			one += holes[p];
			two += holes[7 + p];
		}
		return Margin(holes, me) * 4 + (me == 0 ? one - two : two - one);
	}

	/// <summary>
	/// Legal moves, best guesses first so alpha-beta prunes more: moves that end in your own store
	/// (a sowing laps 13 holes, and the path to your store never crosses theirs), then pits nearest
	/// the store.
	/// </summary>
	private static int Order(int[] holes, int turn, Span<int> order)
	{
		int first = turn == 0 ? 0 : 7;
		int store = first + Kalah.PitsPerSide;
		int count = 0;
		for (int p = store - 1; p >= first; p--)
		{
			if (holes[p] > 0 && holes[p] % 13 == store - p)
				order[count++] = p;
		}
		for (int p = store - 1; p >= first; p--)
		{
			if (holes[p] > 0 && holes[p] % 13 != store - p)
				order[count++] = p;
		}
		return count;
	}
}
