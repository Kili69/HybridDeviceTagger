namespace HybridDeviceTagger.Services;

public interface IPasswordGenerator
{
    string Generate(int length);
}