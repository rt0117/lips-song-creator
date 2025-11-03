namespace LipsSongExtractor.LipsObjects;

public class LpsMusicInfo
{
    public uint   UintID          { get; set; }
    public string Title           { get; set; }
    public string Artist          { get; set; }
    public string Album           { get; set; }
    public string Genre           { get; set; }
    public uint   Year            { get; set; }
    public uint   Rating          { get; set; }
    public uint   Length          { get; set; } 
    public uint   Color           { get; set; }
    public uint   Language        { get; set; }
    public string AudioUri        { get; set; }
}