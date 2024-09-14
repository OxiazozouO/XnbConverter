namespace Squish;

public class BoolFlag
{
	public bool isColourClusterFit;

	public bool isColourIterativeClusterFit;

	public bool isColourRangeFit;

	public bool isDxt1;

	public bool isDxt3;

	public bool isDxt5;

	public bool isWeightColourByAlpha;

	public BoolFlag(SquishFlags flags)
	{
		isDxt1 = (flags & SquishFlags.kDxt1) != 0;
		isDxt3 = (flags & SquishFlags.kDxt3) != 0;
		isDxt5 = (flags & SquishFlags.kDxt5) != 0;
		isColourIterativeClusterFit = (flags & SquishFlags.kColourIterativeClusterFit) != 0;
		isColourClusterFit = (flags & SquishFlags.kColourClusterFit) != 0;
		isColourRangeFit = (flags & SquishFlags.kColourRangeFit) != 0;
		isWeightColourByAlpha = (flags & SquishFlags.kWeightColourByAlpha) != 0;
	}
}
