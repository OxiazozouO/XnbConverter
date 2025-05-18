namespace Squish;

[Flags]
public enum SquishFlags
{
	kDxt1 = 1,
	kDxt3 = 2,
	kDxt5 = 4,
	kColourIterativeClusterFit = 0x100,
	kColourClusterFit = 8,
	kColourRangeFit = 0x10,
	kWeightColourByAlpha = 0x80
}
