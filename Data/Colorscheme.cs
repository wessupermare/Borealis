namespace Borealis.Data;

public record struct Colorscheme(
	Color Aircraft,

	Color Route,
	Color Departure,
	Color Arrival,
	Color Approach,

	Color ClassB,
	Color ClassC,
	Color ClassD,
	Color ClassE,

	Color Airport,
	
	Color Runway,
	Color Taxiway,
	Color Taxilane,
	Color Gate,
	Color Apron,
	Color Building,

	Color FirBoundary,
	Color Coastline,

	Color Cursor,
	Color QDM,
	Color RangeRings,
	Color Background
)
{ }
