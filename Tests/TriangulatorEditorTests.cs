using NUnit.Framework;
using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class TriangulatorTestExtensions
    {
        public static (int, int, int)[] ToTrisTuple(this NativeList<int> triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .OrderBy(i => i.Item1).ThenBy(i => i.Item2).ThenBy(i => i.Item3)
            .ToArray();
    }

    public class TriangulatorEditorTests
    {
        private (int, int, int)[] Triangles => triangulator.Output.Triangles.ToTrisTuple();
        private float2[] Positions => triangulator.Output.Positions.ToArray();

        private Triangulator triangulator;

        [SetUp]
        public void SetUp()
        {
            triangulator = new Triangulator(capacity: 1024, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            triangulator?.Dispose();
        }

        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            triangulator.Settings.RefineMesh = false;
            var input = triangulator.Input;
            input.Positions = positions;

            triangulator.Run();

            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1

            Assert.That(Triangles, Is.EqualTo(new[] { (1, 0, 2), (2, 0, 3) }));
        }

        private static readonly TestCaseData[] validateInputPositionsTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 1 (points count less than 3)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 0),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 2 (duplicated position)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NaN),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 3 (point with NaN)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.PositiveInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with +inf)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NegativeInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with -inf)" },
        };

        [Test, TestCaseSource(nameof(validateInputPositionsTestData))]
        public void ValidateInputPositionsTest(float2[] managedPositions)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            triangulator.Settings.RefineMesh = false;
            triangulator.Input = new() { Positions = positions };
            Assert.Throws<ArgumentException>(() => triangulator.Run());
        }

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.RefineMesh = true;
            settings.MinimumAngle = math.radians(30);
            settings.MinimumArea = 0.3f;
            settings.MaximumArea = 0.3f;

            var input = triangulator.Input;
            input.Positions = positions;

            triangulator.Run();

            ///  3 ------- 2
            ///  |` .   . `|
            ///  |    4    |
            ///  |. `   ` .|
            ///  0 ------- 1

            var expectedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1),
                math.float2(0.5f, 0.5f)
            };
            Assert.That(Positions, Is.EqualTo(expectedPositions));

            var expectedTriangles = new[]
            {
                (1, 0, 4), (2, 1, 4), (3, 2, 4), (4, 0, 3)
            };
            Assert.That(Triangles, Is.EqualTo(expectedTriangles));
        }

        private static readonly TestCaseData[] edgeConstraintsTestData = new[]
        {
            new TestCaseData(
                //   6 ----- 5 ----- 4
                //   |    .`   `.    |
                //   | .`         `. |
                //   7 ------------- 3
                //   | `.         .` |
                //   |    `.   .`    |
                //   0 ----- 1 ----- 2
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 1, 5 })
            {
                TestName = "Test case 1",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (1, 7, 5),
                    (2, 1, 3),
                    (4, 3, 5),
                    (5, 3, 1),
                    (6, 5, 7),
                }
            },

            new TestCaseData(
                //   6 ----- 5 ----- 4
                //   |    .`   `.    |
                //   | .`         `. |
                //   7 ------------- 3
                //   | `.         .` |
                //   |    `.   .`    |
                //   0 ----- 1 ----- 2
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 1, 5, 1, 4 })
            {
                TestName = "Test case 2",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (1, 5, 4),
                    (1, 7, 5),
                    (2, 1, 3),
                    (4, 3, 1),
                    (6, 5, 7),
                }
            },

            new TestCaseData(
                //   9 ----- 8 ----- 7 ----- 6
                //   |    .` `   . ``  `.    |
                //   | .`  :   ..         `. |
                //  10    :  ..        ..... 5
                //   |   :` .   ....`````    |
                //   | ,.:..`````            |
                //  11 ..................... 4
                //   | `. ` . .           .` |
                //   |    `.    ` . .  .`    |
                //   0 ----- 1 ----- 2 ----- 3
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(3, 0),
                    math.float2(3, 1),
                    math.float2(3, 2),
                    math.float2(3, 3),
                    math.float2(2, 3),
                    math.float2(1, 3),
                    math.float2(0, 3),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 3, 9, 8, 5 })
            {
                TestName = "Test case 3",
                ExpectedResult = new[]
                {
                    (1, 0, 11),
                    (2, 1, 11),
                    (3, 2, 11),
                    (3, 10, 9),
                    (3, 11, 10),
                    (6, 5, 7),
                    (8, 4, 3),
                    (8, 5, 4),
                    (8, 7, 5),
                    (9, 8, 3),
                }
            },
        };

        [Test, TestCaseSource(nameof(edgeConstraintsTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            return Triangles;
        }

        private static readonly TestCaseData[] validateConstraintDelaunayTriangulationTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2, 1, 3 }
            ) { TestName = "Test Case 1 (edge-edge intersection)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2, 0, 2 }
            ) { TestName = "Test Case 2 (duplicated edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 0 }
            ) { TestName = "Test Case 3 (zero-length edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0.5f, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2 }
            ) { TestName = "Test Case 4 (edge collinear with other point)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 5, 1 }
            ) { TestName = "Test Case 5 (odd number of elements in constraints buffer)" },
        };

        [Test, TestCaseSource(nameof(validateConstraintDelaunayTriangulationTestData))]
        public void ValidateConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            Assert.Throws<ArgumentException>(() => triangulator.Run());
        }

        private static readonly TestCaseData[] constraintDelaunayTriangulationWithRefinementTestData = new[]
        {
            //  3 -------- 2
            //  | ' .      |
            //  |    ' .   |
            //  |       ' .|
            //  0 -------- 1
            new TestCaseData(
                new []
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                new []
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,
                    0, 2
                },
                new []
                {
                    math.float2(0.5f, 0.5f)
                }
            )
            {
                TestName = "Test case 1 (square)",
                ExpectedResult = new []
                {
                    (1, 0, 4),
                    (2, 1, 4),
                    (3, 2, 4),
                    (4, 0, 3),
                }
            },

            //  5 -------- 4 -------- 3
            //  |       . '|      . ' |
            //  |    . '   |   . '    |
            //  |. '      .|. '       |
            //  0 -------- 1 -------- 2
            new TestCaseData(
                new []
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new []
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 4,
                    4, 5,
                    5, 0,
                    0, 3
                },
                new []
                {
                    math.float2(1, 0.5f),
                    math.float2(0.5f, 0),
                    math.float2(0.5f, 0.25f),
                    math.float2(0.75f, 0.625f),
                    math.float2(0.5f, 1),
                    math.float2(1.5f, 1),
                    math.float2(1.5f, 0.75f),
                    math.float2(1.25f, 0.375f),
                    math.float2(1.5f, 0),
                }
            )
            {
                TestName = "Test case 2 (rectangle)",
                ExpectedResult = new []
                {
                    (3, 2, 12),
                    (6, 1, 8),
                    (6, 4, 12),
                    (7, 0, 8),
                    (8, 0, 5),
                    (8, 1, 7),
                    (8, 5, 10),
                    (9, 4, 6),
                    (9, 6, 8),
                    (9, 8, 10),
                    (10, 4, 9),
                    (11, 3, 12),
                    (12, 2, 14),
                    (12, 4, 11),
                    (13, 1, 6),
                    (13, 6, 12),
                    (13, 12, 14),
                    (14, 1, 13)
                }
            },
        };

        [Test, TestCaseSource(nameof(constraintDelaunayTriangulationWithRefinementTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationWithRefinementTest(float2[] managedPositions, int[] constraints, float2[] insertedPoints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = true;
            settings.MinimumArea = .125f;
            settings.MaximumArea = .250f;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            Assert.That(Positions, Is.EqualTo(managedPositions.Union(insertedPoints)));

            return Triangles;
        }
    }
}
