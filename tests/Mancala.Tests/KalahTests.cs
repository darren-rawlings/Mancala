using System.Linq;
using Mancala.Core;
using Renegade.Core;
using Xunit;

namespace Mancala.Tests;

public class KalahTests
{
	[Fact]
	public void StartsWithFourInEveryPitAndEmptyStores()
	{
		var game = new Kalah();
		for (int hole = 0; hole < Kalah.Holes; hole++)
			Assert.Equal(Kalah.IsStore(hole) ? 0 : 4, game[hole]);
		Assert.Equal(Side.One, game.Turn);
	}

	[Fact]
	public void SowsOneStoneIntoEachFollowingHole()
	{
		var game = new Kalah();
		var result = game.Sow(0);
		Assert.Equal(new[] { 1, 2, 3, 4 }, result.Path);
		Assert.Equal(0, game[0]);
		Assert.Equal(5, game[4]);
		Assert.Equal(Side.Two, game.Turn);
	}

	[Fact]
	public void LastStoneInOwnStoreGivesAnotherTurn()
	{
		var game = new Kalah();
		var result = game.Sow(2); // 4 stones: 3, 4, 5, store
		Assert.True(result.ExtraTurn);
		Assert.Equal(1, game.Store(Side.One));
		Assert.Equal(Side.One, game.Turn);
	}

	[Fact]
	public void SkipsTheOpponentsStore()
	{
		var game = Setup(one: new[] { 0, 0, 0, 0, 0, 10 }, two: new[] { 1, 1, 1, 1, 1, 1 });
		var result = game.Sow(5); // 10 stones from the last pit: store, 7..12, (skip 13), 0, 1
		Assert.DoesNotContain(Kalah.StoreTwo, result.Path);
		Assert.Equal(new[] { 6, 7, 8, 9, 10, 11, 12, 0, 1 }, result.Path.Take(9));
		Assert.Equal(10, result.Path.Count);
	}

	[Fact]
	public void LandingInAnEmptyOwnPitCapturesTheOppositePit()
	{
		// One sows pit 1 (1 stone) into empty pit 2; opposite pit 10 has 5 stones.
		var game = Setup(one: new[] { 3, 1, 0, 2, 2, 2 }, two: new[] { 2, 2, 2, 5, 2, 2 });
		var result = game.Sow(1);
		Assert.Equal(Kalah.Opposite(2), result.CaptureFrom);
		Assert.Equal(6, result.Captured);
		Assert.Equal(6, game.Store(Side.One));
		Assert.Equal(0, game[2]);
		Assert.Equal(0, game[10]);
	}

	[Fact]
	public void NoCaptureWhenTheOppositePitIsEmpty()
	{
		var game = Setup(one: new[] { 3, 1, 0, 2, 2, 2 }, two: new[] { 2, 2, 0, 0, 2, 2 });
		var result = game.Sow(1);
		Assert.Equal(-1, result.CaptureFrom);
		Assert.Equal(1, game[2]);
	}

	[Fact]
	public void GameEndsWhenASideIsEmptyAndTheRestIsBanked()
	{
		var game = Setup(one: new[] { 0, 0, 0, 0, 0, 1 }, two: new[] { 1, 2, 3, 0, 0, 0 });
		var result = game.Sow(5); // the last stone goes to One's store: One's side is now empty
		Assert.True(result.GameOver);
		Assert.True(game.IsOver);
		Assert.Equal(6, game.Store(Side.Two));
		Assert.Equal(1, game.Store(Side.One));
		Assert.Equal(Side.Two, game.Winner);
	}

	[Fact]
	public void StonesAreNeverLostOrMade()
	{
		var game = new Kalah();
		var computer = new ComputerPlayer(Difficulty.Medium, seed: 7);
		while (!game.IsOver)
		{
			game.Sow(computer.ChooseMove(game));
			Assert.Equal(48, Enumerable.Range(0, Kalah.Holes).Sum(h => game[h]));
		}
	}

	[Fact]
	public void UndoRestoresTheBoardAndTurn()
	{
		var game = new Kalah();
		game.Sow(0);
		game.Sow(9);
		Assert.True(game.Undo());
		Assert.True(game.Undo());
		Assert.Equal(new Kalah()[0], game[0]);
		Assert.Equal(Side.One, game.Turn);
		Assert.False(game.Undo());
	}

	[Fact]
	public void HardBeatsEasyMostOfTheTime()
	{
		int hardWins = 0;
		for (int i = 0; i < 10; i++)
		{
			var game = new Kalah();
			var hard = new ComputerPlayer(Difficulty.Hard, seed: i);
			var easy = new ComputerPlayer(Difficulty.Easy, seed: 100 + i);
			bool hardIsOne = i % 2 == 0;
			while (!game.IsOver)
			{
				bool hardToMove = (game.Turn == Side.One) == hardIsOne;
				game.Sow((hardToMove ? hard : easy).ChooseMove(game));
			}
			if (game.Winner == (hardIsOne ? Side.One : Side.Two))
				hardWins++;
		}
		Assert.True(hardWins >= 8, $"Hard won only {hardWins} of 10.");
	}

	[Fact]
	public void HardThinksQuicklyEnoughForAPhone()
	{
		// About 12 ms a move on average on a desktop; phones are a few times slower. A full game
		// must stay well clear of a noticeable pause on every move.
		var game = new Kalah();
		var hard = new ComputerPlayer(Difficulty.Hard, seed: 1);
		long worst = 0;
		while (!game.IsOver)
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			int move = hard.ChooseMove(game);
			worst = System.Math.Max(worst, watch.ElapsedMilliseconds);
			game.Sow(move);
		}
		Assert.True(worst < 400, $"Slowest move took {worst} ms.");
	}

	[Fact]
	public void StandardCaptureNeedsMarblesOppositeButEmptyCaptureDoesNot()
	{
		// One's last marble lands in its own empty pit 3; the pit opposite (9) is empty.
		var standard = Setup(new[] { 0, 0, 1, 0, 0, 0 }, new[] { 1, 1, 0, 1, 1, 1 });
		Assert.Equal(0, standard.Sow(2).Captured);
		var variant = Setup(new[] { 0, 0, 1, 0, 0, 0 }, new[] { 1, 1, 0, 1, 1, 1 }, new KalahRules(EmptyCapture: true));
		var result = variant.Sow(2);
		Assert.Equal(1, result.Captured);
		Assert.Equal(1, variant.Store(Side.One));
		Assert.Equal(0, variant[3]);
	}

	[Theory]
	[InlineData(Leftovers.Owner, 0, 5)]
	[InlineData(Leftovers.Clearer, 5, 0)]
	public void LeftoversGoToTheirOwnerOrToWhoeverClearedTheirSide(Leftovers rule, int oneGets, int twoGets)
	{
		// One sows its last marble into its store: One's side is clear, Two still has 5.
		var game = Setup(new[] { 0, 0, 0, 0, 0, 1 }, new[] { 2, 3, 0, 0, 0, 0 }, new KalahRules(Leftovers: rule));
		var result = game.Sow(5);
		Assert.True(result.GameOver);
		Assert.Equal(1 + oneGets, game.Store(Side.One));
		Assert.Equal(twoGets, game.Store(Side.Two));
		Assert.All(result.FinalSweep, sweep => Assert.Equal(rule == Leftovers.Clearer ? Kalah.StoreOne : Kalah.StoreTwo, sweep.Store));
	}

	[Theory]
	[InlineData(3)]
	[InlineData(6)]
	public void AGameCanStartWithThreeToSixMarblesAPit(int stones)
	{
		var game = new Kalah(new KalahRules(StonesPerPit: stones));
		Assert.All(Kalah.PitsOf(Side.One).Concat(Kalah.PitsOf(Side.Two)), pit => Assert.Equal(stones, game[pit]));
	}

	[Theory]
	[InlineData(false, Leftovers.Owner)]
	[InlineData(true, Leftovers.Clearer)]
	public void TheComputerFinishesGamesUnderEveryRuleSet(bool emptyCapture, Leftovers leftovers)
	{
		var rules = new KalahRules(5, emptyCapture, leftovers);
		var game = new Kalah(rules);
		var one = new ComputerPlayer(Difficulty.Hard, seed: 2);
		var two = new ComputerPlayer(Difficulty.Medium, seed: 3);
		int guard = 0;
		while (!game.IsOver && guard++ < 300)
			game.Sow((game.Turn == Side.One ? one : two).ChooseMove(game));
		Assert.True(game.IsOver);
		Assert.Equal(60, game.Store(Side.One) + game.Store(Side.Two));
	}

	/// <summary>A position with the given pits (left to right from each player's view), One to move.</summary>
	private static Kalah Setup(int[] one, int[] two, KalahRules? rules = null)
	{
		var game = new Kalah((rules ?? KalahRules.Standard) with { StonesPerPit = 0 });
		var field = typeof(Kalah).GetField("_holes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
		var holes = (int[])field.GetValue(game)!;
		for (int i = 0; i < 6; i++)
		{
			holes[i] = one[i];
			holes[7 + i] = two[i];
		}
		return game;
	}
}
