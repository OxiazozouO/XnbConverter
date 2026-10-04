using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using XnbConverter.Configurations;

namespace XnbConverter.Utilities;

/// <summary>
/// 从 PE 文件里提取**嵌入的托管程序集**（如 Terraria.exe 内嵌的 ReLogic.dll）。
///
/// 全程只用 PE / 元数据读取器，**不执行目标文件的任何代码**；
/// 也不受"该程序集是不是本机能加载的框架"影响 —— 只认元数据。
/// </summary>
public static class EmbeddedAssemblies
{
	/// <summary>一个被提取出来的嵌入程序集。</summary>
	public readonly struct Extracted
	{
		public readonly string ResourceName;

		public readonly string OutputPath;

		public readonly int Length;

		public Extracted(string resourceName, string outputPath, int length)
		{
			ResourceName = resourceName;
			OutputPath = outputPath;
			Length = length;
		}
	}

	/// <summary>该文件是不是托管程序集（有 CLI 头 + 元数据）。</summary>
	public static bool IsManagedAssembly(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			using PEReader peReader = new PEReader(stream);
			if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader == null)
			{
				return false;
			}

			peReader.GetMetadataReader();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// 提取 <paramref name="path"/> 中所有内嵌的托管程序集到 <paramref name="outputDir"/>。
	/// 非托管文件、没有嵌入资源的文件都只是返回空列表。
	/// </summary>
	public static List<Extracted> Extract(string path, string outputDir)
	{
		List<Extracted> list = new List<Extracted>();
		try
		{
			using FileStream stream = File.OpenRead(path);
			using PEReader peReader = new PEReader(stream);
			if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader == null)
			{
				return list;
			}

			MetadataReader metadataReader = peReader.GetMetadataReader();
			int relativeVirtualAddress = peReader.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress;
			if (relativeVirtualAddress <= 0)
			{
				return list;
			}

			PEMemoryBlock sectionData = peReader.GetSectionData(relativeVirtualAddress);
			Directory.CreateDirectory(outputDir);

			foreach (ManifestResourceHandle handle in metadataReader.ManifestResources)
			{
				ManifestResource resource = metadataReader.GetManifestResource(handle);
				string name = metadataReader.GetString(resource.Name);

				// Implementation 非空表示该资源在别的程序集里，不是内嵌
				if (!resource.Implementation.IsNil)
				{
					continue;
				}

				int offset = (int)resource.Offset;
				if (offset < 0 || offset + 4 > sectionData.Length)
				{
					continue;
				}

				int length = sectionData.GetReader(offset, 4).ReadInt32();
				if (length <= 0 || offset + 4 + length > sectionData.Length)
				{
					continue;
				}

				byte[] bytes = sectionData.GetContent(offset + 4, length).ToArray();

				// 只挑出真正是托管程序集的那些（MZ 头 + BSJB 元数据签名）
				if (bytes.Length < 2 || bytes[0] != 0x4D || bytes[1] != 0x5A)
				{
					continue;
				}

				string fileName = Path.GetFileName(name);
				if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				{
					fileName += ".dll";
				}

				string outputPath = Path.Combine(outputDir, fileName);
				File.WriteAllBytes(outputPath, bytes);
				list.Add(new Extracted(name, outputPath, bytes.Length));
				Logger.Info(Error.EmbeddedAssemblies_1, name, bytes.Length, outputPath);
			}
		}
		catch (Exception ex)
		{
			Logger.Warn(Error.EmbeddedAssemblies_2, path, ex.Message);
		}

		return list;
	}
}
