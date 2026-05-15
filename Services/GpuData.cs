namespace GameOptimizer.Services;

public record GpuData(
    string Vendor,
    int Temp,
    int GpuUtil,
    int MemUtil,
    int MemUsedMB,
    int MemTotMB,
    double PowerW,
    double PowerLimW,
    int ClkMhz,
    int MemClkMhz);
