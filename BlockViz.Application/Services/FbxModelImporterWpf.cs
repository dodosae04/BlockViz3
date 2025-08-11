using System;
using System.IO;
using System.Linq;
using Ai = Assimp;
using Media = System.Windows.Media;
using Media3D = System.Windows.Media.Media3D;

namespace BlockViz.Applications.Services
{
    /// <summary>
    /// AssimpNet으로 FBX를 로드해 WPF Model로 변환.
    /// - 모든 회전 적용 후, 공장판 "윗면"을 y=0에 정렬 (블록/작업장 바닥과 동일 평면)
    /// - 추가 Z축 회전(-90°)을 고정으로 더해 "왼쪽으로 눕히기" 효과
    ///   * 반대로 눕히고 싶으면 EXTRA_Z_DEG 값을 +90으로 변경
    /// </summary>
    public static class FbxModelImporterWpf
    {
        // ← 여기만 바꾸면 눕히는 방향을 바꿀 수 있음
        private const double EXTRA_X_DEG = 0;    // 필요시 추가 X 회전
        private const double EXTRA_Y_DEG = 0;    // 필요시 추가 Y 회전
        private const double EXTRA_Z_DEG = -90;  // 왼쪽으로 눕히기(반대로는 +90)

        /// <param name="rotXDeg">기본 X축 회전(도)</param>
        /// <param name="rotYDeg">기본 Y축 회전(도)</param>
        /// <param name="rotZDeg">기본 Z축 회전(도)</param>
        /// <param name="alignGroundTopToZero">
        /// true: 회전 후 모델의 "윗면"을 y=0으로 이동(바닥과 같은 평면)
        /// false: 최저면(minY)을 y=0으로 이동
        /// </param>
        public static Media3D.ModelVisual3D LoadAsVisual(
            string filePath,
            double rotXDeg = -90,
            double rotYDeg = 90,
            double rotZDeg = 0,
            bool alignGroundTopToZero = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("FBX 파일을 찾을 수 없습니다.", filePath);

            using var ctx = new Ai.AssimpContext();
            var scene = ctx.ImportFile(
                filePath,
                Ai.PostProcessSteps.Triangulate
                | Ai.PostProcessSteps.JoinIdenticalVertices
                | Ai.PostProcessSteps.GenerateNormals
                | Ai.PostProcessSteps.FlipUVs
                | Ai.PostProcessSteps.CalculateTangentSpace
            );
            if (scene == null || scene.MeshCount == 0)
                throw new InvalidOperationException("FBX에서 유효한 메시를 찾지 못했습니다.");

            // 1) WPF Model로 변환
            var group = new Media3D.Model3DGroup();
            var mats = scene.Materials.Select(ConvertWpfMaterial).ToArray();

            foreach (var mesh in scene.Meshes)
            {
                var geo = new Media3D.MeshGeometry3D
                {
                    Positions = new Media3D.Point3DCollection(mesh.Vertices.Count),
                    TriangleIndices = new Media.Int32Collection()
                };

                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    Ai.Vector3D v = mesh.Vertices[i];
                    geo.Positions.Add(new Media3D.Point3D(v.X, v.Y, v.Z));
                }

                if (mesh.HasNormals)
                {
                    geo.Normals = new Media3D.Vector3DCollection(mesh.Normals.Count);
                    for (int i = 0; i < mesh.Normals.Count; i++)
                    {
                        Ai.Vector3D n = mesh.Normals[i];
                        geo.Normals.Add(new Media3D.Vector3D(n.X, n.Y, n.Z));
                    }
                }

                foreach (var f in mesh.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        geo.TriangleIndices.Add(f.Indices[0]);
                        geo.TriangleIndices.Add(f.Indices[1]);
                        geo.TriangleIndices.Add(f.Indices[2]);
                    }
                }

                geo.Freeze();

                Media3D.Material mat = mats[Math.Max(0, mesh.MaterialIndex)];
                var gm = new Media3D.GeometryModel3D(geo, mat) { BackMaterial = mat };
                gm.Freeze();
                group.Children.Add(gm);
            }
            group.Freeze();

            // 2) 회전(기존 rotX/Y/Z + 추가 EXTRA 회전)
            var rot = new Media3D.Transform3DGroup();

            if (Math.Abs(rotXDeg) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(1, 0, 0), rotXDeg)));
            if (Math.Abs(rotYDeg) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(0, 1, 0), rotYDeg)));
            if (Math.Abs(rotZDeg) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(0, 0, 1), rotZDeg)));

            // 추가 회전(왼쪽으로 눕히기)
            if (Math.Abs(EXTRA_X_DEG) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(1, 0, 0), EXTRA_X_DEG)));
            if (Math.Abs(EXTRA_Y_DEG) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(0, 1, 0), EXTRA_Y_DEG)));
            if (Math.Abs(EXTRA_Z_DEG) > 1e-6)
                rot.Children.Add(new Media3D.RotateTransform3D(
                    new Media3D.AxisAngleRotation3D(new Media3D.Vector3D(0, 0, 1), EXTRA_Z_DEG)));

            // 3) 회전 적용 좌표계에서 바닥 정렬 오프셋 계산
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            var yVals = new System.Collections.Generic.List<double>(4096);

            foreach (var mesh in scene.Meshes)
            {
                foreach (var v in mesh.Vertices)
                {
                    var p = rot.Transform(new Media3D.Point3D(v.X, v.Y, v.Z));
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Z < minZ) minZ = p.Z;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Z > maxZ) maxZ = p.Z;
                    yVals.Add(p.Y);
                }
            }

            yVals.Sort();
            double eps = 1e-4;
            double groundTopY = minY; // fallback
            for (int i = 0; i < yVals.Count; i++)
            {
                if (yVals[i] > minY + eps)
                {
                    groundTopY = yVals[i];
                    break;
                }
            }

            double centerX = (minX + maxX) * 0.5;
            double centerZ = (minZ + maxZ) * 0.5;

            var align = new Media3D.Transform3DGroup();
            align.Children.Add(rot); // 회전 먼저
            align.Children.Add(new Media3D.TranslateTransform3D(
                -centerX,
                -(alignGroundTopToZero ? groundTopY : minY),
                -centerZ
            ));

            return new Media3D.ModelVisual3D
            {
                Content = group,
                Transform = align
            };
        }

        private static Media3D.Material ConvertWpfMaterial(Ai.Material m)
        {
            Ai.Color4D dc = m.HasColorDiffuse ? m.ColorDiffuse : new Ai.Color4D(0.7f, 0.7f, 0.7f, 1f);
            Media.Color color = Media.Color.FromScRgb(dc.A, dc.R, dc.G, dc.B);

            var brush = new Media.SolidColorBrush(color);
            brush.Freeze();

            var mat = new Media3D.DiffuseMaterial(brush);
            mat.Freeze();
            return mat;
        }
    }
}
