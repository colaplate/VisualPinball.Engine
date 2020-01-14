using System.IO;
using VisualPinball.Engine.Game;

namespace VisualPinball.Engine.VPT.Bumper
{
	public class Bumper : Item<BumperData>, IRenderable
	{
		private readonly BumperMeshGenerator _meshGenerator;

		public Bumper(BinaryReader reader, string itemName) : base(new BumperData(reader, itemName))
		{
			_meshGenerator = new BumperMeshGenerator(Data);
		}

		public RenderObject[] GetRenderObjects(Table.Table table, Origin origin = Origin.Global)
		{
			return _meshGenerator.GetRenderObjects(table, origin);
		}
	}
}