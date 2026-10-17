using System;
using System.Collections.Generic;
using System.Linq;

namespace Mancala.Core;

public enum Side
{
	One,
	Two,
}

/// <summary>
/// What one sowing did, step by step, so the game can animate exactly what the rules decided.
/// <see cref="Path"/> lists the hole each stone landed in, in order.
/// </summary>
/// <summary>Where the marbles left in the pits go when a side runs out.</summary>
public enum Leftovers
{
	/// <summary>Standard: each player keeps the marbles on their own side.</summary>
	Owner,

	/// <summary>Variant: the player who cleared their side takes everything left.</summary>
	Clearer,
}

/// <summary>
/// The house rules. Standard Kalah is 4 marbles a pit, a capture only with marbles opposite, and
/// leftovers kept by their owner; the variants are the common ones (see Kalah on Wikipedia).
/// </summary>
public sealed record KalahRules(int StonesPerPit = 4, bool EmptyCapture = false, Leftovers Leftovers = Leftovers.Owner)
{
	public static readonly KalahRules Standard = new();
}

public sealed record SowResult(
	Side Mover,
	int From,
	int Picked,
	IReadOnlyList<int> Path,
	bool ExtraTurn,
	int CaptureFrom,
	int Captured,
	bool GameOver,
	IReadOnlyList<(int Hole, int Stones, int Store)> FinalSweep);

/// <summary>
/// Kalah, the mancala most people know: two rows of six pits plus a store each, 4 stones per pit.
/// Holes are numbered counter-clockwise: 0-5 are side One's pits (left to right from One's view),
/// 6 is One's store, 7-12 are Two's pits, 13 is Two's store. See docs/DESIGN.md for the rules.
/// </summary>
public sealed class Kalah
{
	public const int PitsPerSide = 6;
	public const int Holes = 14;
	public const int StoreOne = 6;
	public const int StoreTwo = 13;

	private readonly int[] _holes = new int[Holes];
	private readonly Stack<(int[] Holes, Side Turn, bool Over)> _history = new();

	public Side Turn { get; private set; }
	public bool IsOver { get; private set; }
	public KalahRules Rules { get; }
	public int StonesPerPit => Rules.StonesPerPit;

	public Kalah(int stonesPerPit = 4) : this(KalahRules.Standard with { StonesPerPit = stonesPerPit }) { }

	public Kalah(KalahRules rules)
	{
		Rules = rules;
		Reset();
	}

	private Kalah(Kalah other)
	{
		Rules = other.Rules;
		Array.Copy(other._holes, _holes, Holes);
		Turn = other.Turn;
		IsOver = other.IsOver;
	}

	public int this[int hole] => _holes[hole];

	/// <summary>The 14 hole counts, for the computer's fast search.</summary>
	public int[] CopyHoles() => (int[])_holes.Clone();

	public int MovesMade => _history.Count;

	public void Reset(Side first = Side.One)
	{
		for (int i = 0; i < Holes; i++)
			_holes[i] = IsStore(i) ? 0 : StonesPerPit;
		Turn = first;
		IsOver = false;
		_history.Clear();
	}

	/// <summary>A copy without history, for the computer to search.</summary>
	public Kalah Clone() => new(this);

	public static bool IsStore(int hole) => hole == StoreOne || hole == StoreTwo;

	public static int StoreOf(Side side) => side == Side.One ? StoreOne : StoreTwo;

	public static Side Opponent(Side side) => side == Side.One ? Side.Two : Side.One;

	/// <summary>The hole opposite a pit across the board (0↔12, 1↔11 … 5↔7).</summary>
	public static int Opposite(int pit) => 12 - pit;

	public static Side OwnerOf(int hole) => hole <= StoreOne ? Side.One : Side.Two;

	/// <summary>The pit numbers (0-13 scale) belonging to a side, in sowing order.</summary>
	public static IEnumerable<int> PitsOf(Side side) => Enumerable.Range(side == Side.One ? 0 : 7, PitsPerSide);

	public int Store(Side side) => _holes[StoreOf(side)];

	public bool CanSow(int pit) => !IsOver && !IsStore(pit) && OwnerOf(pit) == Turn && _holes[pit] > 0;

	public IEnumerable<int> LegalMoves() => PitsOf(Turn).Where(CanSow);

	/// <summary>The side ahead when the game is over, or null for a draw (or while still playing).</summary>
	public Side? Winner
	{
		get
		{
			if (!IsOver || Store(Side.One) == Store(Side.Two))
				return null;
			return Store(Side.One) > Store(Side.Two) ? Side.One : Side.Two;
		}
	}

	public SowResult Sow(int pit)
	{
		if (!CanSow(pit))
			throw new InvalidOperationException($"Can't sow hole {pit} for {Turn}.");
		_history.Push(((int[])_holes.Clone(), Turn, IsOver));

		var mover = Turn;
		int picked = _holes[pit];
		_holes[pit] = 0;
		var path = new List<int>(picked);
		int hole = pit;
		int skip = StoreOf(Opponent(mover));
		for (int left = picked; left > 0; left--)
		{
			do
				hole = (hole + 1) % Holes;
			while (hole == skip);
			_holes[hole]++;
			path.Add(hole);
		}

		bool extraTurn = hole == StoreOf(mover);
		int captureFrom = -1, captured = 0;
		if (!extraTurn && !IsStore(hole) && OwnerOf(hole) == mover && _holes[hole] == 1 && (Rules.EmptyCapture || _holes[Opposite(hole)] > 0))
		{
			// The last marble and everything opposite go to the store (with Empty capture, even
			// when nothing is opposite: the lone marble is taken).
			captureFrom = Opposite(hole);
			captured = _holes[captureFrom] + 1;
			_holes[StoreOf(mover)] += captured;
			_holes[captureFrom] = 0;
			_holes[hole] = 0;
		}

		var sweep = new List<(int, int, int)>();
		bool oneEmpty = PitsOf(Side.One).All(p => _holes[p] == 0), twoEmpty = PitsOf(Side.Two).All(p => _holes[p] == 0);
		if (oneEmpty || twoEmpty)
		{
			IsOver = true;
			var clearer = oneEmpty ? Side.One : Side.Two;
			for (int p = 0; p < Holes; p++)
			{
				if (IsStore(p) || _holes[p] == 0)
					continue;
				int store = StoreOf(Rules.Leftovers == Leftovers.Clearer ? clearer : OwnerOf(p));
				sweep.Add((p, _holes[p], store));
				_holes[store] += _holes[p];
				_holes[p] = 0;
			}
		}
		else if (!extraTurn)
		{
			Turn = Opponent(mover);
		}

		return new SowResult(mover, pit, picked, path, extraTurn, captureFrom, captured, IsOver, sweep);
	}

	/// <summary>Takes back the last sowing. Returns false if there is nothing to undo.</summary>
	public bool Undo()
	{
		if (_history.Count == 0)
			return false;
		var (holes, turn, over) = _history.Pop();
		Array.Copy(holes, _holes, Holes);
		Turn = turn;
		IsOver = over;
		return true;
	}

	/// <summary>Who moved last, if anyone: the side to move before the most recent sowing.</summary>
	public Side? LastMover => _history.Count == 0 ? null : _history.Peek().Turn;
}
