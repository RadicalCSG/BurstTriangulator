using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System;
using System.Linq;

#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    [BurstCompile]
    public static class BurstCompileStaticMethodsTests
    {
        [BurstCompile]
        public static void ArgsBlitableTest(ref Args args) { }
    }

    public class UnsafeTriangulatorEditorTests
    {
        [Test]
        public void ArgsDefaultTest()
        {
            var settings = new TriangulationSettings();
            var args = Args.Default();

            Assert.That(args.Preprocessor, Is.EqualTo(settings.Preprocessor));
            Assert.That(args.SloanMaxIters, Is.EqualTo(settings.SloanMaxIters));
            Assert.That(args.AutoHolesAndBoundary, Is.EqualTo(settings.AutoHolesAndBoundary));
            Assert.That(args.RefineMesh, Is.EqualTo(settings.RefineMesh));
            Assert.That(args.RestoreBoundary, Is.EqualTo(settings.RestoreBoundary));
            Assert.That(args.ValidateInput, Is.EqualTo(settings.ValidateInput));
            Assert.That(args.Verbose, Is.EqualTo(settings.Verbose));
            Assert.That(args.ConcentricShellsParameter, Is.EqualTo(settings.ConcentricShellsParameter));
            Assert.That(args.RefinementThresholdAngle, Is.EqualTo(settings.RefinementThresholds.Angle));
            Assert.That(args.RefinementThresholdArea, Is.EqualTo(settings.RefinementThresholds.Area));
        }

        [Test] public void ArgsImplicitSettingsCastTest() => Assert.That((Args)new TriangulationSettings(), Is.EqualTo(Args.Default()));

        [Test] public void ArgsWithTest([Values] bool value) => Assert.That(Args.Default().With(autoHolesAndBoundary: value).AutoHolesAndBoundary, Is.EqualTo(value));

        [BurstCompile]
        private struct ArgsWithJob : IJob
        {
            public NativeReference<Args> argsRef;
            public void Execute() => argsRef.Value = argsRef.Value.With(autoHolesAndBoundary: true);
        }

        [Test]
        public void ArgsWithJobTest()
        {
            using var argsRef = new NativeReference<Args>(Args.Default(autoHolesAndBoundary: false), Allocator.Persistent);
            new ArgsWithJob { argsRef = argsRef }.Run();
            Assert.That(argsRef.Value.AutoHolesAndBoundary, Is.True);
        }

        [BurstCompile]
        private struct UnsafeTriangulatorWithTempAllocatorJob : IJob
        {
            public NativeArray<float2> positions;
            public NativeArray<int> constraints;
            public NativeList<int> triangles;

            public void Execute()
            {
                LowLevel.Unsafe.Extensions.Triangulate(new UnsafeTriangulator<float2>(),
                    input: new() { Positions = positions, ConstraintEdges = constraints },
                    output: new() { Triangles = triangles },
                    args: Args.Default(),
                    allocator: Allocator.Temp
                );
            }
        }

        [Test]
        public void UsingTempAllocatorInJobTest()
        {
            // When using Temp allocation e.g. for native it can throw exception:
            //
            // ```
            // InvalidOperationException: The Unity.Collections.NativeList`1[System.Int32]
            // has been declared as [WriteOnly] in the job, but you are reading from it.
            // ```
            //
            // This seems to be a known issue in current Unity.Collections package
            // https://docs.unity3d.com/Packages/com.unity.collections@2.2/manual/issues.html
            //
            // ```
            // All containers allocated with Allocator.Temp on the same thread use a shared
            // AtomicSafetyHandle instance rather than each having their own. Most of the time,
            // this isn't an issue because you can't pass Temp allocated collections into a job.
            // 
            // However, when you use Native*HashMap, NativeParallelMultiHashMap, Native*HashSet,
            // and NativeList together with their secondary safety handle, this shared AtomicSafetyHandle
            // instance is a problem.
            //
            // A secondary safety handle ensures that a NativeArray which aliases a NativeList
            // is invalidated when the NativeList is reallocated due to resizing
            // ```
            using var positions = new NativeArray<float2>(new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            new UnsafeTriangulatorWithTempAllocatorJob { positions = positions, constraints = constraints, triangles = triangles }.Run();
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
    [TestFixture(typeof(int2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class UnsafeTriangulatorEditorTests<T> where T : unmanaged
    {
        [Test]
        public void UnsafeTriangulatorOutputPositionsTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Positions = outputPositions },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(outputPositions.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Positions.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputHalfedgesTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var halfedges = new NativeList<int>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Halfedges = halfedges },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(halfedges.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Halfedges.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputConstrainedHalfedgesTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(constrainedHalfedges.AsArray().ToArray(), Is.EqualTo(triangulator.Output.ConstrainedHalfedges.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputStatusTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var status = new NativeReference<Status>(Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Status = status },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(status.Value, Is.EqualTo(triangulator.Output.Status.Value));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsAutoTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args.With(autoHolesAndBoundary: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(autoHolesAndBoundary: false), Allocator.Persistent);
            t.PlantHoleSeeds(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(autoHolesAndBoundary: true), Allocator.Persistent);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsRestoreBoundaryTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args.With(restoreBoundary: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(restoreBoundary: false), Allocator.Persistent);
            t.PlantHoleSeeds(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(restoreBoundary: true), Allocator.Persistent);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsHolesTest()
        {
            var t = new UnsafeTriangulator<T>();
            var inputWithHoles = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
                HoleSeeds = LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h3),
            };
            var inputWithoutHoles = inputWithHoles;
            inputWithoutHoles.HoleSeeds = default;
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(inputWithHoles, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(inputWithoutHoles, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions }, args, Allocator.Persistent);
            t.PlantHoleSeeds(inputWithHoles, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions }, args, Allocator.Persistent);

            h1.Free();
            h2.Free();
            h3.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorAutoHolesWithIgnoredConstraintsTest()
        {
            // 3 ------------------------ 2
            // |                          |
            // |   5                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |          9 ---- 8    |
            // |   |          |      |    |
            // |   |          |      |    |
            // |   4          6 ---- 7    |
            // |                          |
            // 0 ------------------------ 1
            using var positions = new NativeArray<T>(new float2[]
            {
                math.float2(0, 0),
                math.float2(10, 0),
                math.float2(10, 10),
                math.float2(0, 10),

                math.float2(1, 1),
                math.float2(1, 9),

                math.float2(8, 1),
                math.float2(9, 1),
                math.float2(9, 2),
                math.float2(8, 2),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5,
                6, 7, 7, 8, 8, 9, 9, 6,
            }, Allocator.Persistent);
            using var ignoreConstraints = new NativeArray<bool>(new bool[]
            {
                false, false, false, false,
                true,
                false, false, false, false,
            }, Allocator.Persistent);


            var t = new UnsafeTriangulator<T>();

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraintEdges, IgnoreConstraintForPlantingSeeds = ignoreConstraints },
                output: new() { Triangles = triangles1 },
                args: Args.Default().With(autoHolesAndBoundary: true), Allocator.Persistent
            );

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var ignoredHalfedges = new NativeList<bool>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraintEdges, IgnoreConstraintForPlantingSeeds = ignoreConstraints },
                output: new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions, IgnoredHalfedgesForPlantingSeeds = ignoredHalfedges },
                args: Args.Default(), Allocator.Persistent
            );
            t.PlantHoleSeeds(
                input: default,
                output: new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions, IgnoredHalfedgesForPlantingSeeds = ignoredHalfedges },
                args: Args.Default().With(autoHolesAndBoundary: true), Allocator.Persistent
            );

            Assert.That(triangles1.AsArray(), Has.Length.EqualTo(3 * 12));
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray()).Using(TrianglesComparer.Instance));
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class UnsafeTriangulatorEditorTestsWithRefinement<T> where T : unmanaged
    {
        [Test]
        public void UnsafeTriangulatorOutputTrianglesTest([Values] bool constrain, [Values] bool refine, [Values] bool holes)
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            if (typeof(T) == typeof(fp2) && constrain && refine && !holes)
            {
                Assert.Ignore(
                    "This input gets stuck with this configuration.\n" +
                    "\n" +
                    "Explanation: When constraints and refinement are enabled, but restore boundary is not, \n" +
                    "the refinement procedure can quickly get stuck and produce an excessive number of triangles. \n" +
                    "According to the literature, there are many examples suggesting that one should plant holes first, \n" +
                    "then refine the mesh. These small triangles fall outside of `fp2` precision."
                );
            }
#endif

            using var positions = new NativeArray<T>(LakeSuperior.Points.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constrain ? constraints : default, HoleSeeds = holes ? holesSeeds : default },
                Settings = { RefineMesh = refine, RestoreBoundary = holes },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constrain ? constraints : default, HoleSeeds = holes ? holesSeeds : default },
                output: new() { Triangles = triangles },
                args: Args.Default(refineMesh: refine, restoreBoundary: holes),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(triangles.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Triangles.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorRefineMeshTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
            };

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, Args.Default(validateInput: false, refineMesh: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T>
            {
                Triangles = triangles2,
                Halfedges = halfedges,
                Positions = outputPositions,
                ConstrainedHalfedges = constrainedHalfedges
            };

            t.Triangulate(input, new() { Triangles = triangles2, Positions = outputPositions, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges }, Args.Default(validateInput: false, refineMesh: false), Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent, constrainBoundary: true);

            h1.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorRefineMeshConstrainedTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false, refineMesh: true, restoreBoundary: true);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T>
            {
                Triangles = triangles2,
                Halfedges = halfedges,
                Positions = outputPositions,
                ConstrainedHalfedges = constrainedHalfedges
            };

            t.Triangulate(input, new() { Triangles = triangles2, Positions = outputPositions, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges }, args.With(refineMesh: false), Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent, constrainBoundary: false);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsRefineMeshTest()
        {
            var t = new UnsafeTriangulator<T>();
            var inputWithHoles = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
                HoleSeeds = LakeSuperior.Holes.DynamicCast<T>().AsNativeArray(out var h3),
            };
            var inputWithoutHoles = inputWithHoles;
            inputWithoutHoles.HoleSeeds = default;
            var args = Args.Default(validateInput: false, refineMesh: true, restoreBoundary: true);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(inputWithHoles, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T> { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions };
            t.Triangulate(inputWithoutHoles, output, args.With(refineMesh: false), Allocator.Persistent);
            t.PlantHoleSeeds(inputWithHoles, output, args, Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent);

            h1.Free();
            h2.Free();
            h3.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void DynamicInsertTest()
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(3, 0),
                new(3, 3),
                new(0, 3),

                new(1, 1),
                new(2, 1),
                new(2, 2),
                new(1, 2),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new LowLevel.Unsafe.InputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new LowLevel.Unsafe.OutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            int FindTriangle(ReadOnlySpan<int> initialTriangles, int j)
            {
                var (s0, s1, s2) = (initialTriangles[3 * j + 0], initialTriangles[3 * j + 1], initialTriangles[3 * j + 2]);
                for (int i = 0; i < triangles.Length / 3; i++)
                {
                    var (t0, t1, t2) = (triangles[3 * i + 0], triangles[3 * i + 1], triangles[3 * i + 2]);
                    if (t0 == s0 && t1 == s1 && t2 == s2)
                    {
                        return i;
                    }
                }
                return -1;
            }

            t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            var random = new Unity.Mathematics.Random(seed: 42);
            for (int iter = 0; iter < 5; iter++)
            {
                using var initialTriangles = triangles.ToArray(Allocator.Persistent);
                for (int j = 0; j < initialTriangles.Length / 3; j++)
                {
                    var i = FindTriangle(initialTriangles, j);
                    if (i != -1)
                    {
                        t.DynamicInsertPoint(output, tId: i, bar: random.NextBarcoords3(), allocator: Allocator.Persistent);
                    }
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 4f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }
        }

        [Test]
        public void DynamicSplitTest([Values] bool bulk)
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0, 0, 2,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new LowLevel.Unsafe.InputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new LowLevel.Unsafe.OutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            t.Triangulate(input, output, args: Args.Default(), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            for (int iter = 0; iter < 4; iter++)
            {
                do_iter(iter);
            }

            void do_iter(int iter)
            {
                var count = 0;
                // If `bulk` is enabled we split 2^iter **diagonal** halfedges, where target length is sqrt(2) / 2^iter, otherwise
                // we split 4^(iter+1) **boundary**  halfedges, where target length is 1 / 2^iter.
                var dist = (bulk ? math.sqrt(2) : 1f) / (1 << iter);
                while (count < (bulk ? 1 : 4) << iter)
                {
                    for (int he = 0; he < triangles.Length; he++)
                    {
                        var ell = len(he);
                        if (constrainedHalfedges[he] && (bulk ? halfedges[he] != -1 : halfedges[he] == -1) && math.abs(ell - dist) <= math.EPSILON)
                        {
                            t.DynamicSplitHalfedge(output, he, 0.5f, Allocator.Persistent);
                            count++;
                        }
                    }
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 2f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }

            float len(int he)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (p, q) = (outputPositions[i].ToFloat2(), outputPositions[j].ToFloat2());
                return math.distance(p, q);
            }

            static int NextHalfedge(int he) => he % 3 == 2 ? he - 2 : he + 1;
        }

        [Test]
        public void DynamicSplitRandomTest()
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(3, 0),
                new(3, 3),
                new(0, 3),

                new(1, 1),
                new(2, 1),
                new(2, 2),
                new(1, 2),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new LowLevel.Unsafe.InputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new LowLevel.Unsafe.OutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            var random = new Unity.Mathematics.Random(seed: 42);

            for (int iter = 0; iter < 3; iter++)
            {
                for (int i = 0; i < 16; i++)
                {
                    var he = Find();
                    var alpha = random.NextFloat(0.1f, 0.9f);
                    t.DynamicSplitHalfedge(output, he, alpha, Allocator.Persistent);
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 4f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }

            int Find()
            {
                var maxLen = float.MinValue;
                var maxHe = -1;
                for (int he = 0; he < triangles.Length; he++)
                {
                    if (!constrainedHalfedges[he])
                    {
                        continue;
                    }

                    var ell = len(he);
                    if (ell > maxLen)
                    {
                        (maxHe, maxLen) = (he, ell);
                    }
                }
                return maxHe;
            }

            float len(int he)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (p, q) = (outputPositions[i].ToFloat2(), outputPositions[j].ToFloat2());
                return math.distance(p, q);
            }

            static int NextHalfedge(int he) => he % 3 == 2 ? he - 2 : he + 1;
        }
    }
}
