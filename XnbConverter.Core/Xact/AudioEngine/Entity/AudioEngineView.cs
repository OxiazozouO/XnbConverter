using Newtonsoft.Json;

namespace XnbConverter.Xact.AudioEngine.Entity;

/// <summary>
/// .xgs 的可编辑视图：只包含已建模、且修改后不改变文件布局的字段。
/// 打包时把视图里的值套回重新解析出的实体上，再按原偏移写回。
/// </summary>
public class AudioEngineView
{
	public class CategoryView
	{
		public string Name;

		public int MaxInstances;

		public float FadeInMs;

		public float FadeOutMs;

		public byte InstanceFlags;

		public ushort Unknown;

		public byte VolumeDecibels;

		public byte VisibilityFlags;
	}

	public class VariableView
	{
		public int Index;

		public string Name;

		public byte Flags;

		public double InitValue;

		public double MinValue;

		public double MaxValue;
	}

	public class PointView
	{
		public double X;

		public double Y;

		public AudioEngine.RpcPointType Type;
	}

	public class CurveView
	{
		public bool IsGlobal;

		public string VariableName;

		public AudioEngine.RpcParameter Parameter;

		public List<PointView> Points = new List<PointView>();
	}

	public List<CategoryView> Categories = new List<CategoryView>();

	public List<CurveView> RpcCurves = new List<CurveView>();

	public List<VariableView> Variables = new List<VariableView>();

	public static AudioEngineView From(AudioEngine engine)
	{
		AudioEngineView audioEngineView = new AudioEngineView();
		foreach (AudioCategory audioCategory in engine._categories)
		{
			audioEngineView.Categories.Add(new CategoryView
			{
				Name = audioCategory.name,
				MaxInstances = audioCategory.maxInstances,
				FadeInMs = audioCategory.fadeIn * 1000f,
				FadeOutMs = audioCategory.fadeOut * 1000f,
				InstanceFlags = audioCategory.instanceFlags,
				Unknown = audioCategory.unkn,
				VolumeDecibels = audioCategory.volumeDecibels,
				VisibilityFlags = audioCategory.visibilityFlags
			});
		}

		audioEngineView.AddVariables(engine._variables, true);
		audioEngineView.AddVariables(engine._cueVariables, false);
		audioEngineView.Variables.Sort((a, b) => a.Index.CompareTo(b.Index));

		foreach (AudioEngine.RpcCurve rpcCurve in engine.RpcCurves)
		{
			AudioEngine.RpcVariable rpcVariable = rpcCurve.IsGlobal
				? engine._variables[rpcCurve.Variable]
				: engine._cueVariables[rpcCurve.Variable];
			CurveView curveView = new CurveView
			{
				IsGlobal = rpcCurve.IsGlobal,
				VariableName = rpcVariable.Name,
				Parameter = rpcCurve.Parameter
			};
			foreach (AudioEngine.RpcPoint rpcPoint in rpcCurve.Points)
			{
				curveView.Points.Add(new PointView
				{
					X = rpcPoint.X,
					Y = rpcPoint.Y,
					Type = rpcPoint.Type
				});
			}

			audioEngineView.RpcCurves.Add(curveView);
		}

		return audioEngineView;
	}

	private void AddVariables(AudioEngine.RpcVariable[] variables, bool isGlobal)
	{
		if (variables == null)
		{
			return;
		}

		foreach (AudioEngine.RpcVariable rpcVariable in variables)
		{
			Variables.Add(new VariableView
			{
				Index = rpcVariable.Index,
				Name = rpcVariable.Name,
				Flags = rpcVariable.Flags,
				InitValue = rpcVariable.InitValue,
				MinValue = rpcVariable.MinValue,
				MaxValue = rpcVariable.MaxValue
			});
		}
	}

	/// <summary>把视图中的值套回实体（实体应当是刚 Read 出来的，偏移完整）。</summary>
	public void ApplyTo(AudioEngine engine)
	{
		if (Categories.Count != engine._categories.Length)
		{
			throw new XnbConverter.Exceptions.XnbError(
				$"视图里有 {Categories.Count} 个类别，与文件中的 {engine._categories.Length} 个不符，当前只支持同尺寸修改");
		}

		for (int i = 0; i < Categories.Count; i++)
		{
			CategoryView categoryView = Categories[i];
			AudioCategory audioCategory = engine._categories[i];
			audioCategory.name = categoryView.Name;
			audioCategory.maxInstances = categoryView.MaxInstances;
			audioCategory.instanceLimit = categoryView.MaxInstances != 255;
			audioCategory.fadeIn = categoryView.FadeInMs / 1000f;
			audioCategory.fadeOut = categoryView.FadeOutMs / 1000f;
			audioCategory.instanceFlags = categoryView.InstanceFlags;
			audioCategory.fadeType = (AudioCategory.CrossfadeType)(categoryView.InstanceFlags & 7);
			audioCategory.InstanceBehavior = (AudioCategory.MaxInstanceBehavior)(categoryView.InstanceFlags >> 3);
			audioCategory.unkn = categoryView.Unknown;
			audioCategory.volumeDecibels = categoryView.VolumeDecibels;
			audioCategory.visibilityFlags = categoryView.VisibilityFlags;
			audioCategory.isBackgroundMusic = (categoryView.VisibilityFlags & 1) != 0;
			audioCategory.isPublic = (categoryView.VisibilityFlags & 2) != 0;
		}

		Dictionary<int, VariableView> dictionary = new Dictionary<int, VariableView>();
		foreach (VariableView variable in Variables)
		{
			dictionary[variable.Index] = variable;
		}

		ApplyVariables(engine._variables, dictionary);
		ApplyVariables(engine._cueVariables, dictionary);

		if (RpcCurves.Count != engine.RpcCurves.Length)
		{
			throw new XnbConverter.Exceptions.XnbError(
				$"视图里有 {RpcCurves.Count} 条 RPC 曲线，与文件中的 {engine.RpcCurves.Length} 条不符，当前只支持同尺寸修改");
		}

		for (int j = 0; j < RpcCurves.Count; j++)
		{
			CurveView curveView2 = RpcCurves[j];
			AudioEngine.RpcCurve rpcCurve = engine.RpcCurves[j];
			if (curveView2.Points.Count != rpcCurve.Points.Length)
			{
				throw new XnbConverter.Exceptions.XnbError(
					$"第 {j} 条 RPC 曲线的点数由 {rpcCurve.Points.Length} 变为 {curveView2.Points.Count}，当前只支持同尺寸修改");
			}

			rpcCurve.Parameter = curveView2.Parameter;
			for (int k = 0; k < curveView2.Points.Count; k++)
			{
				rpcCurve.Points[k].X = curveView2.Points[k].X;
				rpcCurve.Points[k].Y = curveView2.Points[k].Y;
				rpcCurve.Points[k].Type = curveView2.Points[k].Type;
			}
		}
	}

	private static void ApplyVariables(AudioEngine.RpcVariable[] variables, Dictionary<int, VariableView> byIndex)
	{
		if (variables == null)
		{
			return;
		}

		foreach (AudioEngine.RpcVariable rpcVariable in variables)
		{
			if (!byIndex.TryGetValue(rpcVariable.Index, out VariableView value))
			{
				continue;
			}

			rpcVariable.Flags = value.Flags;
			rpcVariable.InitValue = value.InitValue;
			rpcVariable.MinValue = value.MinValue;
			rpcVariable.MaxValue = value.MaxValue;
			rpcVariable.IsGlobal = (value.Flags & 4) != 0;
			rpcVariable.IsPublic = (value.Flags & 2) != 0;
			rpcVariable.IsReadOnly = (value.Flags & 1) != 0;
			rpcVariable.IsReserved = (value.Flags & 8) != 0;
		}
	}
}
