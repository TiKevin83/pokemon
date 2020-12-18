using System;
using System.Collections.Generic;

public class RbyData {

    public Charmap Charmap;
    public DataList<RbySpecies> Species = new DataList<RbySpecies>();
    public DataList<RbyMove> Moves = new DataList<RbyMove>();
    public DataList<RbyItem> Items = new DataList<RbyItem>();
    public DataList<RbyTileset> Tilesets = new DataList<RbyTileset>();
    public DataList<RbyTrainerClass> TrainerClasses = new DataList<RbyTrainerClass>();

    public RbyData() {
        Charmap = new Charmap("A B C D E F G H I J K L M N O P " +
                              "Q R S T U V W X Y Z ( ) : ; [ ] " +
                              "a b c d e f g h i j k l m n o p " +
                              "q r s t u v w x y z é 'd 'l 's 't 'v " +
                              "_ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ " +
                              "_ _ _ _ _ _ _ _ _ _ _ _ _ _ _ _ " +
                              "' PK MN - 'r 'm ? ! . _ _ _ _ _ _ M " +
                              "$ * . / , F 0 1 2 3 4 5 6 7 8 9 ");
        Charmap.Map[0x4A] = "PkMn";
        Charmap.Map[0x54] = "POKE";
        Charmap.Map[0x52] = "<PLAYER>";
        Charmap.Map[0x53] = "<RIVAL";

        Species.NameCallback = obj => obj.Name;
        Species.IndexCallback = obj => obj.Id;

        Moves.NameCallback = obj => obj.Name;
        Moves.IndexCallback = obj => obj.Id;

        Items.NameCallback = obj => obj.Name;
        Items.IndexCallback = obj => obj.Id;

        Tilesets.IndexCallback = obj => obj.Id;

        TrainerClasses.NameCallback = obj => obj.Name;
        TrainerClasses.IndexCallback = obj => obj.Id;
    }
}

public class Rby : GameBoy {

    private static Dictionary<int, RbyData> ParsedROMs = new Dictionary<int, RbyData>();
    public RbyData Data;
    public Charmap Charmap {
        get { return Data.Charmap; }
    }

    public DataList<RbySpecies> Species {
        get { return Data.Species; }
    }

    public DataList<RbyMove> Moves {
        get { return Data.Moves; }
    }

    public DataList<RbyItem> Items {
        get { return Data.Items; }
    }

    public DataList<RbyTileset> Tilesets {
        get { return Data.Tilesets; }
    }

    public DataList<RbyTrainerClass> TrainerClasses {
        get { return Data.TrainerClasses; }
    }

    public Rby(string rom, SpeedupFlags speedupFlags = SpeedupFlags.None) : base("roms/gbc_bios.bin", rom, speedupFlags) {
        if(ParsedROMs.ContainsKey(ROM.GlobalChecksum)) {
            Data = ParsedROMs[ROM.GlobalChecksum];
        } else {
            Data = new RbyData();
            LoadMoves();
            LoadSpecies();
            LoadItems();
            LoadTilesets();
            LoadTrainerClasses();
        }
    }

    private void LoadSpecies() {
        const int maxIndexNumber = 190;

        int numBaseStats = (SYM["BaseStatsEnd"] - SYM["BaseStats"]) / (SYM["MonBaseStatsEnd"] - SYM["MonBaseStats"]);
        byte[] pokedex = ROM.Subarray(SYM["PokedexOrder"], maxIndexNumber);
        ByteStream data = ROM.From("BaseStats");

        for(int i = 0; i < numBaseStats; i++) {
            byte indexNumber = (byte) Array.IndexOf(pokedex, data.Peek());
            Species.Add(new RbySpecies(this, ++indexNumber, data));
        }

        // Add Mew data
        Species.Add(new RbySpecies(this, 21, ROM.From(SYM["MewBaseStats"])));

        // Add MISSINGNO data
        for(int i = 1; i <= maxIndexNumber; i++) {
            if(pokedex[i - 1] == 0) {
                RbySpecies species = new RbySpecies(this, (byte) i);
                Species.Add(new RbySpecies(this, (byte) i));
            }
        }
    }

    private void LoadMoves() {
        int movesStart = SYM["Moves"];
        int numMoves = (SYM["BaseStats"] - movesStart) / (SYM["MoveEnd"] - movesStart);

        ByteStream nameStream = ROM.From("MoveNames");
        ByteStream dataStream = ROM.From(movesStart);

        for(int i = 0; i < numMoves; i++) {
            Moves.Add(new RbyMove(this, dataStream, nameStream));
        }
    }

    private void LoadItems() {
        ByteStream nameStream = ROM.From("ItemNames");

        for(int i = 0; i < 256; i++) {
            string name;
            if(i > 0x0 && i <= 0x61) {
                name = Charmap.Decode(nameStream.Until(Charmap.Terminator));
            } else if(i >= 0xC4 && i <= 0xC8) {
                name = String.Format("HM{0}", (i + 1 - 0xc4).ToString("D2"));
            } else if(i >= 0xC9 && i <= 0xFF) {
                name = String.Format("TM{0}", (i + 1 - 0xC9).ToString("D2"));
            } else {
                name = String.Format("hex{0:X2}", i);
            }

            Items.Add(new RbyItem(this, (byte) i, name));
        }
    }

    private void LoadTilesets() {
        Dictionary<byte, List<(byte, byte)>> tilePairCollisionsLand = new Dictionary<byte, List<(byte, byte)>>();
        ByteStream collisionData = ROM.From("TilePairCollisionsLand");

        byte tileset;
        while((tileset = collisionData.u8()) != 0xff) {
            if(!tilePairCollisionsLand.ContainsKey(tileset)) {
                tilePairCollisionsLand[tileset] = new List<(byte, byte)>();
            }
            tilePairCollisionsLand[tileset].Add((collisionData.u8(), collisionData.u8()));
        }

        ByteStream dataStream = ROM.From("Tilesets");
        int numTilesets = GetType() == typeof(Yellow) ? 25 : 24;
        for(byte index = 0; index < numTilesets; index++) {
            List<(byte, byte)> collisions = tilePairCollisionsLand.GetValueOrDefault(index, new List<(byte, byte)>());
            Tilesets.Add(new RbyTileset(this, index, collisions, dataStream));
        }
    }


    private void LoadTrainerClasses() {
        const int numTrainerClasses = 47;

        ByteStream nameStream = ROM.From("TrainerNames");
        ByteStream trainerClassStream = ROM.From("TrainerDataPointers");

        int[] trainerDataOffsets = new int[numTrainerClasses];

        for(int i = 0; i < numTrainerClasses; i++) {
            trainerDataOffsets[i] = 0x0E << 16 | trainerClassStream.u16le();
        }

        for(int trainerClass = 0; trainerClass < numTrainerClasses; trainerClass++) {
            int currentOffset = trainerDataOffsets[trainerClass];
            int nextTrainerOffset = trainerClass == numTrainerClasses - 1 ? SYM["TrainerAI"] : trainerDataOffsets[trainerClass + 1];
            int length = nextTrainerOffset - currentOffset;

            if (length == 0) {
                nameStream.Until(Charmap.Terminator);
                continue;
            }

            ByteStream dataStream = ROM.From(trainerDataOffsets[trainerClass]);
            TrainerClasses.Add(new RbyTrainerClass(this, (byte) (trainerClass + 201), length, dataStream, nameStream));
        }

    public override Font ReadFont() {
        const int numCols = 16;
        byte[] gfx = ROM.Subarray("FontGraphics", SYM["FontGraphicsEnd"] - SYM["FontGraphics"]);
        Bitmap bitmap = new Bitmap(numCols * 8, gfx.Length / numCols);
        for(int i = 0; i < gfx.Length; i++) {
            int xTile = (i / 8 * 8) % bitmap.Width;
            int yTile = i / bitmap.Width * 8;
            for(int j = 0; j < 8; j++) {
                byte col = (byte) ((gfx[i] >> (7 - j) & 0x1) * 0xff);
                bitmap.SetPixel(xTile + j, yTile + i % 8, col, col, col, col);
            }
        }

        return new Font {
            Bitmap = bitmap,
            CharacterSize = 8,
            NumCharsPerRow = numCols,
            Charmap = Data.Charmap,
            CharmapOffset = 0x80,
        };
    }
}