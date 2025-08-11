using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BlockViz.Applications.Helpers;
using BlockViz.Applications.Models;     // BlockProperties, BlockColorMap
using BlockViz.Domain.Models;           // Block
using HelixToolkit.Wpf;

namespace BlockViz.Applications.Services
{
    [Export(typeof(IBlockArrangementService))]
    public class BlockArrangementService : IBlockArrangementService
    {
        // ── 배치 스케일/간격 ───────────────────────────────────────────────
        private const double Scale = 0.15;            // 블록 치수 스케일
        private const double GapRatio = 0.7;          // Breadth 대비 간격 비율
        private const double FactoryModelScale = 0.8; // ← 공장 FBX만 0.5배로 축소 (기존 1.0)
        // ─────────────────────────────────────────────────────────────────

        // 작업장 중심 (원본 좌표계)
        private static readonly Dictionary<int, Point3D> WorkplaceCenters = new()
        {
            {1, new Point3D(-30, 0, -40)},
            {2, new Point3D(-30, 0, -17)},
            {3, new Point3D(-54, 0,  17)},
            {4, new Point3D(-54, 0,  40)},
            {5, new Point3D( 15, 0,  17)},
            {6, new Point3D(  5, 0,  40)},
        };

        // 작업장 크기 (원본 좌표계 X폭, Z깊이)
        private static readonly Dictionary<int, (double X, double Z)> WorkplaceSizes = new()
        {
            {1, (12 * 7, 4 * 5)},
            {2, (12 * 7, 4 * 5)},
            {3, ( 6 * 6, 4 * 5)},
            {4, ( 6 * 6, 4 * 5)},
            {5, ( 6 * 7, 4 * 5)},
            {6, (12 * 6, 4 * 5)},
        };

        private ModelVisual3D? _factoryCached;

        // 메인 엔트리
        public IEnumerable<ModelVisual3D> Arrange(IEnumerable<Block> blocks, DateTime date)
        {
            var visuals = new List<ModelVisual3D>();

            // 1) 라이트 + 공장 FBX(황색 바닥판 대체)
            visuals.Add(new DefaultLights());
            var factory = GetOrLoadFactoryModel();
            if (factory != null) visuals.Add(factory);

            // 2) 작업장 프레임 + 라벨
            for (int id = 1; id <= 6; id++)
            {
                visuals.Add(BuildFrameAtCenter(id));
                visuals.Add(BuildLabelAtTopLeft(id));
            }

            // 3) 현재 날짜에 유효한 블록만
            var live = blocks.Where(b => b.Start <= date && date <= b.End).ToList();

            // 4) 작업장별 배치
            foreach (var wpGroup in live.GroupBy(b => b.DeployWorkplace).OrderBy(g => g.Key))
            {
                int wpId = wpGroup.Key;
                if (!WorkplaceCenters.ContainsKey(wpId)) continue;

                var center = WorkplaceCenters[wpId];
                var size = WorkplaceSizes[wpId];

                double fullWidth = size.X * Scale;
                double trackSpacing = size.Z * Scale; // 트랙 간격: 작업장 깊이

                // 프로젝트(이름) 단위로 겹침 방지 트랙 할당
                var projectGroups = wpGroup
                    .GroupBy(b => b.Name)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Blocks = g.OrderBy(b => b.Start).ToList(),
                        Start = g.Min(b => b.Start),
                        End = g.Max(b => b.End)
                    })
                    .OrderBy(p => p.Start)
                    .ToList();

                var tracks = new List<string>[3] { new(), new(), new() };
                var projectTrack = new Dictionary<string, int>();

                foreach (var proj in projectGroups)
                {
                    int assigned = 2; // 기본 가운데 트랙
                    for (int t = 0; t < 3; t++)
                    {
                        bool ok = tracks[t].All(name =>
                        {
                            var other = projectGroups.First(p => p.Name == name);
                            return other.End <= proj.Start || other.Start >= proj.End;
                        });
                        if (ok) { assigned = t; break; }
                    }
                    tracks[assigned].Add(proj.Name);
                    projectTrack[proj.Name] = assigned;
                }

                // 박스(블록) 생성
                double halfOffset = fullWidth / 2.0;
                var offsets = new[] { -halfOffset, 0.0, halfOffset };

                foreach (var proj in projectGroups)
                {
                    int tIdx = projectTrack[proj.Name];
                    double z = center.Z + (tIdx - 1) * trackSpacing;

                    var widths = proj.Blocks.Select(b => b.Length * Scale).ToList();
                    var gaps = proj.Blocks.Select(b => b.Breadth * Scale * GapRatio).ToList();
                    double span = widths.Sum() + gaps.Sum();

                    double startX = center.X + offsets[tIdx] - span / 2.0;
                    double accX = startX;

                    for (int i = 0; i < proj.Blocks.Count; i++)
                    {
                        var blk = proj.Blocks[i];
                        accX += gaps[i];

                        double w = blk.Length * Scale;
                        double d = blk.Breadth * Scale;
                        double h = blk.Height * Scale;

                        double xC = accX + w / 2.0;
                        double yC = h / 2.0;      // 바닥(y=0)에 접촉

                        var box = new BoxVisual3D
                        {
                            Center = new Point3D(xC, yC, z),
                            Width = w,
                            Length = d,
                            Height = h,
                            Material = MaterialHelper.CreateMaterial(BlockColorMap.GetColor(blk.Name)),
                            BackMaterial = MaterialHelper.CreateMaterial(BlockColorMap.GetColor(blk.Name))
                        };

                        var mv = new ModelVisual3D();
                        mv.Children.Add(box);
                        BlockProperties.SetData(mv, blk);
                        visuals.Add(mv);

                        accX += w;
                    }
                }
            }

            return visuals;
        }

        // ─────────────────────────────────────────────────────────────────────
        // 공장 모델 로딩/배치 (황색 바닥판 대체)
        //  - Importer에서 윗면을 y=0으로 정렬
        //  - 모델만 0.5배 축소(작업장/블록 스케일에는 영향 없음)
        //  - Y축 180° 회전 후 y:-0.8로 내림
        // ─────────────────────────────────────────────────────────────────────
        private ModelVisual3D? GetOrLoadFactoryModel()
        {
            if (_factoryCached != null) return _factoryCached;

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "A.fbx");
            if (!File.Exists(path)) return null;

            // 좌표계 보정
            const double ROT_X = -90; // FBX → WPF(Y-up) 보정
            const double ROT_Y = 90; // 작업장 축 보정
            const double ROT_Z = 0;

            // 공장판 "윗면"을 y=0에 정렬해서 로드
            var visual = FbxModelImporterWpf.LoadAsVisual(
                path,
                rotXDeg: ROT_X,
                rotYDeg: ROT_Y,
                rotZDeg: ROT_Z,
                alignGroundTopToZero: true
            );

            // 작업장 전체 사각형의 중앙(XZ)에 위치 + 요구 변환(스케일 → Y축 180° → 평면 아래 -0.8로 이동)
            var (cx, cz) = ComputeFactoryCenterXZ();
            var tg = new Transform3DGroup();

            // 공장 모델만 0.5배 축소
            if (Math.Abs(FactoryModelScale - 1.0) > 1e-6)
                tg.Children.Add(new ScaleTransform3D(FactoryModelScale, FactoryModelScale, FactoryModelScale));

            // 병뚜껑 돌리듯: 평면 법선(Y축) 기준 180°
            tg.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180)));

            // 작업장 기준으로 0.8만큼 아래로
            tg.Children.Add(new TranslateTransform3D(cx, -0.8, cz));

            // 기존(Importer) 변환과 합성
            if (visual.Transform is Transform3DGroup g1)
            {
                var composed = new Transform3DGroup();
                foreach (var t in g1.Children) composed.Children.Add(t);
                foreach (var t in tg.Children) composed.Children.Add(t);
                visual.Transform = composed;
            }
            else
            {
                var composed = new Transform3DGroup();
                composed.Children.Add(visual.Transform ?? Transform3D.Identity);
                foreach (var t in tg.Children) composed.Children.Add(t);
                visual.Transform = composed;
            }

            _factoryCached = visual;
            return _factoryCached;
        }

        // 작업장 범위로부터 “공장 중앙(XZ)” 계산
        private (double X, double Z) ComputeFactoryCenterXZ()
        {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;

            foreach (var kv in WorkplaceCenters)
            {
                var c = kv.Value;
                var s = WorkplaceSizes[kv.Key];
                minX = Math.Min(minX, c.X - s.X / 2);
                maxX = Math.Max(maxX, c.X + s.X / 2);
                minZ = Math.Min(minZ, c.Z - s.Z / 2);
                maxZ = Math.Max(maxZ, c.Z + s.Z / 2);
            }

            return ((minX + maxX) / 2.0, (minZ + maxZ) / 2.0);
        }

        // 작업장 외곽 프레임(테두리 선)
        private ModelVisual3D BuildFrameAtCenter(int id)
        {
            var c = WorkplaceCenters[id];
            var s = WorkplaceSizes[id];
            double hx = s.X / 2, hz = s.Z / 2;

            var lines = new LinesVisual3D { Color = Colors.DimGray, Thickness = 2 };
            var p1 = new Point3D(c.X - hx, 0, c.Z - hz);
            var p2 = new Point3D(c.X + hx, 0, c.Z - hz);
            var p3 = new Point3D(c.X + hx, 0, c.Z + hz);
            var p4 = new Point3D(c.X - hx, 0, c.Z + hz);

            lines.Points.Add(p1); lines.Points.Add(p2);
            lines.Points.Add(p2); lines.Points.Add(p3);
            lines.Points.Add(p3); lines.Points.Add(p4);
            lines.Points.Add(p4); lines.Points.Add(p1);

            return lines;
        }

        // 작업장 라벨
        private ModelVisual3D BuildLabelAtTopLeft(int id)
        {
            var c = WorkplaceCenters[id];
            var s = WorkplaceSizes[id];
            double hx = s.X / 2, hz = s.Z / 2;

            return new BillboardTextVisual3D
            {
                Text = $"작업장 {id}",
                Position = new Point3D(c.X - hx + 0.5, 2, c.Z - hz + 0.5),
                Foreground = Brushes.Black,
                Background = Brushes.Transparent,
                FontSize = 14
            };
        }
    }
}
