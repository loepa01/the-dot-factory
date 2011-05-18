﻿/*
 * Copyright 2009, Eran "Pavius" Duchan
 * This file is part of The Dot Factory.
 *
 * The Dot Factory is free software: you can redistribute it and/or modify it 
 * under the terms of the GNU General Public License as published by the Free 
 * Software Foundation, either version 3 of the License, or (at your option) 
 * any later version. The Dot Factory is distributed in the hope that it will be 
 * useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
 * or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more 
 * details. You should have received a copy of the GNU General Public License along 
 * with The Dot Factory. If not, see http://www.gnu.org/licenses/.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;

namespace TheDotFactory
{
    public partial class MainForm : Form
    {
        // formatting strings
        public const string HeaderStringColumnStart = "\t0b";
        public const string HeaderStringColumnMid = ", 0b";
        public const string BitString1 = "1";
        public const string BitString0 = "0";

        // application version
        public const string ApplicationVersion = "0.1.0";

        // current loaded bitmap
        private Bitmap m_currentLoadedBitmap = null;

        // output configuration
        public OutputConfigurationManager m_outputConfigurationManager = new OutputConfigurationManager();

        // output configuration
        private OutputConfiguration m_outputConfig;

        // info per font
        public class FontInfo
        {
            public int                          charHeight;
            public char                         startChar;
            public char                         endChar;
            public CharacterGenerationInfo[]    characters;
            public Font                         font;
            public string                       generatedChars;
        }

        // to allow mapping string/value
        class ComboBoxItem
        {
            public string name;
            public string value;

            // ctor
            public ComboBoxItem(string name, string value)
            {
                this.name = name;
                this.value = value;
            }

            // override ToString() function
            public override string ToString()
            {
                // use name
                return this.name;
            }
        }

        // a bitmap border conta
        class BitmapBorder
        {
            public int bottomY = 0;
            public int rightX = 0;
            public int topY = int.MaxValue;
            public int leftX = int.MaxValue;
        }

        // character generation information
        public class CharacterGenerationInfo
        {
            // pointer the font info
            public FontInfo fontInfo;
            
            // the character
            public char character;

            // the original bitmap
            public Bitmap bitmapOriginal;
            
            // the bitmap to generate into a string (flipped, trimmed - if applicable)
            public Bitmap bitmapToGenerate;

            // value of pages (vertical 8 bits), in serial order from top of bitmap
            public ArrayList pages;

            // character size
            public int width;
            public int height;
            
            // offset into total array
            public int offsetInBytes;
        }

        // strings for comments
        string m_commentStartString = "";
        string m_commentEndString = "";
        string m_commentBlockMiddleString = "";
        string m_commentBlockEndString = "";

        public MainForm()
        {
            InitializeComponent();
        }

        // update input font
        private void updateSelectedFont()
        {
            // set text name in the text box
            txtInputFont.Text = fontDlgInputFont.Font.Name;

            // add to text
            txtInputFont.Text += " " + Math.Round(fontDlgInputFont.Font.Size) + "pts";

            // check if bold
            if (fontDlgInputFont.Font.Bold)
            {
                // add to text
                txtInputFont.Text += " / Bold";
            }

            // check if italic
            if (fontDlgInputFont.Font.Italic)
            {
                // add to text
                txtInputFont.Text += " / Italic";
            }

            // set the font in the text box
            txtInputText.Font = fontDlgInputFont.Font;

            // save into settings
            Properties.Settings.Default.InputFont = fontDlgInputFont.Font;
            Properties.Settings.Default.Save();
        }

        private void btnFontSelect_Click(object sender, EventArgs e)
        {
            // set focus somewhere else
            label1.Focus();
            
            // open font chooser dialog
            if (fontDlgInputFont.ShowDialog() != DialogResult.Cancel)
            {
                updateSelectedFont();
            }
        }

        // populate preformatted text
        private void populateTextInsertCheckbox()
        {
            string all = "", numbers = "", letters = "", uppercaseLetters = "", lowercaseLetters = "", symbols = "";

            // generate characters
            for (char character = ' '; character < 127; ++character)
            {
                // add to all
                all += character;

                // classify letter
                if (Char.IsNumber(character)) numbers += character;
                else if (Char.IsSymbol(character)) symbols += character;
                else if (Char.IsLetter(character) && Char.IsLower(character)) { letters += character; lowercaseLetters += character; }
                else if (Char.IsLetter(character) && !Char.IsLower(character)) { letters += character; uppercaseLetters += character; }
            }

            // add items
            cbxTextInsert.Items.Add(new ComboBoxItem("All", all));
            cbxTextInsert.Items.Add(new ComboBoxItem("Numbers (0-9)", numbers));
            cbxTextInsert.Items.Add(new ComboBoxItem("Letters (A-z)", letters));
            cbxTextInsert.Items.Add(new ComboBoxItem("Lowercase letters (a-z)", lowercaseLetters));
            cbxTextInsert.Items.Add(new ComboBoxItem("Uppercase letters (A-Z)", uppercaseLetters));

            // use first
            cbxTextInsert.SelectedIndex = 0;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // set version
            Text = String.Format("The Dot Factory v.{0}", ApplicationVersion);

            // set input box
            txtInputText.Text = Properties.Settings.Default.InputText;

            // load font
            fontDlgInputFont.Font = Properties.Settings.Default.InputFont;
            updateSelectedFont();

            // load configurations from file
            m_outputConfigurationManager.loadFromFile("OutputConfigs.xml");

            // update the dropdown
            m_outputConfigurationManager.comboboxPopulate(cbxOutputConfiguration);

            // get saved output config index
            int lastUsedOutputConfigurationIndex = Properties.Settings.Default.OutputConfigIndex;

            // load recently used preset
            if (lastUsedOutputConfigurationIndex >= 0 &&
                lastUsedOutputConfigurationIndex < cbxOutputConfiguration.Items.Count)
            {
                // last used
                cbxOutputConfiguration.SelectedIndex = lastUsedOutputConfigurationIndex;

                // load selected configuration
                m_outputConfig = m_outputConfigurationManager.configurationGetAtIndex(lastUsedOutputConfigurationIndex);
            }
            else
            {
                // there's no saved configuration. get the working copy (default)
                m_outputConfig = m_outputConfigurationManager.workingOutputConfiguration;
            }

            // update comment strings
            updateCommentStrings();

            // set checkbox stuff
            populateTextInsertCheckbox();

            // apply font to all appropriate places
            updateSelectedFont();
        }

        // get the characters we need to generate
        string getCharactersToGenerate()
        {
            //
            // iterate through the inputted text and shove to sorted string, removing all duplicates
            //

            // sorted list for insertion/duplication removal
            SortedList<char, char> characterList = new SortedList<char, char>();

            // iterate over the characters in the textbox
            for (int charIndex = 0; charIndex < txtInputText.Text.Length; ++charIndex)
            {
                // get teh char
                char insertionCandidateChar = txtInputText.Text[charIndex];

                // insert the char, if not already in the list and if not space ()
                if (!characterList.ContainsKey(insertionCandidateChar))
                {
                    // check if space character
                    if (insertionCandidateChar == ' ' && !m_outputConfig.generateSpaceCharacterBitmap)
                    {
                        // skip - space is not encoded rather generated dynamically by the driver
                        continue;
                    }

                    // not in list, add
                    characterList.Add(txtInputText.Text[charIndex], ' ');
                }
            }

            // now output the sorted list to a string
            string characterListString = "";

            // iterate over the sorted characters to create the string
            foreach (char characterKey in characterList.Keys)
            {
                // add to string
                characterListString += characterKey;
            }

            // return the character
            return characterListString;
        }

        // convert a letter to bitmap
        private void convertCharacterToBitmap(char character, Font font, out Bitmap outputBitmap, Rectangle largestBitmap)
        {
            // get the string
            string letterString = character.ToString();

            // create bitmap, sized to the correct size
            outputBitmap = new Bitmap((int)largestBitmap.Width, (int)largestBitmap.Height);

            // create grahpics entity for drawing
            Graphics gfx = Graphics.FromImage(outputBitmap);

            // disable anti alias
            gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            // draw centered text
            Rectangle bitmapRect = new System.Drawing.Rectangle(0, 0, outputBitmap.Width, outputBitmap.Height);

            // Set format of string.
            StringFormat drawFormat = new StringFormat();
            drawFormat.Alignment = StringAlignment.Center;
            
            // draw the character
            gfx.FillRectangle(Brushes.White, bitmapRect);
            gfx.DrawString(letterString, font, Brushes.Black, bitmapRect, drawFormat);
        }

        // returns whether a bitmap column is empty (empty means all is back color)
        private bool bitmapColumnIsEmpty(Bitmap bitmap, int column)
        {
            // for each row in the column
            for (int row = 0; row < bitmap.Height; ++row)
            {
                // is the pixel black?
                if (bitmap.GetPixel(column, row).ToArgb() == Color.Black.ToArgb())
                {
                    // found. column is not empty
                    return false;
                }
            }

            // column is empty
            return true;
        }

        // returns whether a bitmap row is empty (empty means all is back color)
        private bool bitmapRowIsEmpty(Bitmap bitmap, int row)
        {
            // for each column in the row
            for (int column = 0; column < bitmap.Width; ++column)
            {
                // is the pixel black?
                if (bitmap.GetPixel(column, row).ToArgb() == Color.Black.ToArgb())
                {
                    // found. column is not empty
                    return false;
                }
            }

            // column is empty
            return true;
        }

        // get the bitmaps border - that is where the black parts start
        private bool getBitmapBorder(Bitmap bitmap, BitmapBorder border)
        {
            // search for first column (x) from the left to contain data
            for (border.leftX = 0; border.leftX < bitmap.Width; ++border.leftX)
            {
                // if found first column from the left, stop looking
                if (!bitmapColumnIsEmpty(bitmap, border.leftX)) break;
            }

            // search for first column (x) from the right to contain data
            for (border.rightX = bitmap.Width - 1; border.rightX >= 0; --border.rightX)
            {
                // if found first column from the left, stop looking
                if (!bitmapColumnIsEmpty(bitmap, border.rightX)) break;
            }

            // search for first row (y) from the top to contain data
            for (border.topY = 0; border.topY < bitmap.Height; ++border.topY)
            {
                // if found first column from the left, stop looking
                if (!bitmapRowIsEmpty(bitmap, border.topY)) break;
            }

            // search for first row (y) from the bottom to contain data
            for (border.bottomY = bitmap.Height - 1; border.bottomY >= 0; --border.bottomY)
            {
                // if found first column from the left, stop looking
                if (!bitmapRowIsEmpty(bitmap, border.bottomY)) break;
            }

            // check if the bitmap contains any black pixels
            if (border.rightX == -1)
            {
                // no pixels were found
                return false;
            }
            else
            {
                // at least one black pixel was found
                return true;
            }
        }

        // iterate through the original bitmaps and find the tightest common border
        private void findTightestCommonBitmapBorder(CharacterGenerationInfo[] charInfoArray,
                                                    ref BitmapBorder tightestBorder)
        {
            // iterate through bitmaps
            for (int charIdx = 0; charIdx < charInfoArray.Length; ++charIdx)
            {
                // create a border
                BitmapBorder bitmapBorder = new BitmapBorder();

                // get the bitmaps border
                getBitmapBorder(charInfoArray[charIdx].bitmapOriginal, bitmapBorder);

                // check if we need to loosen up the tightest border
                tightestBorder.leftX = Math.Min(bitmapBorder.leftX, tightestBorder.leftX);
                tightestBorder.topY = Math.Min(bitmapBorder.topY, tightestBorder.topY);
                tightestBorder.rightX = Math.Max(bitmapBorder.rightX, tightestBorder.rightX);
                tightestBorder.bottomY = Math.Max(bitmapBorder.bottomY, tightestBorder.bottomY);

            }
        }

        // get rotate flip type according to config
        private RotateFlipType getOutputRotateFlipType()
        {
            bool fx = m_outputConfig.flipHorizontal;
            bool fy = m_outputConfig.flipVertical;
            OutputConfiguration.Rotation rot = m_outputConfig.rotation;

            // zero degree rotation
            if (rot == OutputConfiguration.Rotation.RotateZero)
            {
                // return according to flip
                if (!fx && !fy) return RotateFlipType.RotateNoneFlipNone;
                if (fx && !fy) return RotateFlipType.RotateNoneFlipX;
                if (!fx && fy) return RotateFlipType.RotateNoneFlipY;
                if (fx && fy) return RotateFlipType.RotateNoneFlipXY;
            }

            // 90 degree rotation
            if (rot == OutputConfiguration.Rotation.RotateNinety)
            {
                // return according to flip
                if (!fx && !fy) return RotateFlipType.Rotate90FlipNone;
                if (fx && !fy) return RotateFlipType.Rotate90FlipX;
                if (!fx && fy) return RotateFlipType.Rotate90FlipY;
                if (fx && fy) return RotateFlipType.Rotate90FlipXY;
            }

            // 180 degree rotation
            if (rot == OutputConfiguration.Rotation.RotateOneEighty)
            {
                // return according to flip
                if (!fx && !fy) return RotateFlipType.Rotate180FlipNone;
                if (fx && !fy) return RotateFlipType.Rotate180FlipX;
                if (!fx && fy) return RotateFlipType.Rotate180FlipY;
                if (fx && fy) return RotateFlipType.Rotate180FlipXY;
            }

            // 270 degree rotation
            if (rot == OutputConfiguration.Rotation.RotateTwoSeventy)
            {
                // return according to flip
                if (!fx && !fy) return RotateFlipType.Rotate270FlipNone;
                if (fx && !fy) return RotateFlipType.Rotate270FlipX;
                if (!fx && fy) return RotateFlipType.Rotate270FlipY;
                if (fx && fy) return RotateFlipType.Rotate270FlipXY;
            }

            // unknown case, but just return no flip
            return RotateFlipType.RotateNoneFlipNone;
        }

        // generate the bitmap we will then use to convert to string (remove pad, flip)
        private bool manipulateBitmap(Bitmap bitmapOriginal, 
                                      BitmapBorder tightestCommonBorder,
                                      out Bitmap bitmapManipulated,
                                      int minWidth, int minHeight)
        {
            //
            // First, crop
            //

            // get bitmap border - this sets teh crop rectangle to per bitmap, essentially
            BitmapBorder bitmapCropBorder = new BitmapBorder();
            if (getBitmapBorder(bitmapOriginal, bitmapCropBorder) == false && minWidth == 0 && minHeight == 0)
            {
                // no data
                bitmapManipulated = null;

                // bitmap contains no data
                return false;
            }

            // check that width exceeds minimum
            if (bitmapCropBorder.rightX - bitmapCropBorder.leftX + 1 < 0)
            {
                // replace
                bitmapCropBorder.leftX = 0;
                bitmapCropBorder.rightX = minWidth - 1;
            }

            // check that height exceeds minimum
            if (bitmapCropBorder.bottomY - bitmapCropBorder.topY + 1 < 0)
            {
                // replace
                bitmapCropBorder.topY = 0;
                bitmapCropBorder.bottomY = minHeight - 1;
            }

            // should we crop hotizontally according to common
            if (m_outputConfig.paddingRemovalHorizontal == OutputConfiguration.PaddingRemoval.Fixed)
            {
                // cropped Y is according to common
                bitmapCropBorder.topY = tightestCommonBorder.topY;
                bitmapCropBorder.bottomY = tightestCommonBorder.bottomY;
            }
            // check if no horizontal crop is required
            else if (m_outputConfig.paddingRemovalHorizontal == OutputConfiguration.PaddingRemoval.None)
            {
                // set y to actual max border of bitmap
                bitmapCropBorder.topY = 0;
                bitmapCropBorder.bottomY = bitmapOriginal.Height - 1;
            }

            // should we crop vertically according to common
            if (m_outputConfig.paddingRemovalVertical == OutputConfiguration.PaddingRemoval.Fixed)
            {
                // cropped X is according to common
                bitmapCropBorder.leftX = tightestCommonBorder.leftX;
                bitmapCropBorder.rightX = tightestCommonBorder.rightX;
            }
            // check if no vertical crop is required
            else if (m_outputConfig.paddingRemovalVertical == OutputConfiguration.PaddingRemoval.None)
            {
                // set x to actual max border of bitmap
                bitmapCropBorder.leftX = 0;
                bitmapCropBorder.rightX = bitmapOriginal.Width - 1;
            }

            // now copy the output bitmap, cropped as required, to a temporary bitmap
            Rectangle rect = new Rectangle(bitmapCropBorder.leftX, 
                                            bitmapCropBorder.topY,
                                            bitmapCropBorder.rightX - bitmapCropBorder.leftX + 1,
                                            bitmapCropBorder.bottomY - bitmapCropBorder.topY + 1);

            // clone the cropped bitmap into the generated one
            bitmapManipulated = bitmapOriginal.Clone(rect, bitmapOriginal.PixelFormat);

            // get rotate type
            RotateFlipType flipType = getOutputRotateFlipType();
            
            // flip the cropped bitmap
            bitmapManipulated.RotateFlip(flipType);

            // bitmap contains data
            return true;
        }

        // create the page array
        private void convertBitmapToPageArray(Bitmap bitmapToGenerate, out ArrayList pages)
        {
            // create pages
            pages = new ArrayList();

            // for each row
            for (int row = 0; row < bitmapToGenerate.Height; row++)
            {
                // current byte value
                byte currentValue = 0, bitsRead = 0;

                // for each column
                for (int column = 0; column < bitmapToGenerate.Width; ++column) 
                {
                    // is pixel set?
                    if (bitmapToGenerate.GetPixel(column, row).ToArgb() == Color.Black.ToArgb())
                    {
                        // set the appropriate bit in the page
                        if (m_outputConfig.byteOrder == OutputConfiguration.ByteOrder.MsbFirst) currentValue |= (byte)(1 << (7 - bitsRead));
                        else currentValue |= (byte)(1 << bitsRead);
                    }

                    // increment number of bits read
                    ++bitsRead;

                    // have we filled a page?
                    if (bitsRead == 8)
                    {
                        // add byte to page array
                        pages.Add(currentValue);

                        // zero out current value
                        currentValue = 0;

                        // zero out bits read
                        bitsRead = 0;
                    }
                }

                // if we have bits left, add it as is
                if (bitsRead != 0) pages.Add(currentValue);
            }
        }

        // get absolute height/width of characters
        private void getAbsoluteCharacterDimensions(ref Bitmap charBitmap, ref int width, ref int height)
        {
            // check if bitmap exists, otherwise set as zero
            if (charBitmap == null)
            {
                // zero height
                width = 0;
                height = 0;
            }
            else
            {
                // get the absolute font character height. Depends on rotation
                if (m_outputConfig.rotation == OutputConfiguration.Rotation.RotateZero ||
                    m_outputConfig.rotation == OutputConfiguration.Rotation.RotateOneEighty)
                {
                    // if char is not rotated or rotated 180deg, its height is the actual height
                    height = charBitmap.Height;
                    width = charBitmap.Width;
                }
                else
                {
                    // if char is rotated by 90 or 270, its height is the width of the rotated bitmap
                    height = charBitmap.Width;
                    width = charBitmap.Height;
                }
            }
        }

        // get font info from string
        private void populateFontInfoFromCharacters(ref FontInfo fontInfo)
        {
            // do nothing if no chars defined
            if (fontInfo.characters.Length == 0) return;
            
            // total offset
            int charByteOffset = 0;
            int dummy = 0;

            // set start char
            fontInfo.startChar = (char)0xFFFF;
            fontInfo.endChar = ' ';

            // the fixed absolute character height
            // int fixedAbsoluteCharHeight;
            getAbsoluteCharacterDimensions(ref fontInfo.characters[0].bitmapToGenerate, ref dummy, ref fontInfo.charHeight);
                
            // iterate through letter string
            for (int charIdx = 0; charIdx < fontInfo.characters.Length; ++charIdx)
            {
                // skip empty bitmaps
                if (fontInfo.characters[charIdx].bitmapToGenerate == null) continue;

                // get char
                char currentChar = fontInfo.characters[charIdx].character;

                // is this character smaller than start char?
                if (currentChar < fontInfo.startChar) fontInfo.startChar = currentChar;

                // is this character bigger than end char?
                if (currentChar > fontInfo.endChar) fontInfo.endChar = currentChar;

                // populate number of rows
                getAbsoluteCharacterDimensions(ref fontInfo.characters[charIdx].bitmapToGenerate, 
                                                ref fontInfo.characters[charIdx].width,
                                                ref fontInfo.characters[charIdx].height); 

                // populate offset of character
                fontInfo.characters[charIdx].offsetInBytes = charByteOffset;

                // increment byte offset
                charByteOffset += fontInfo.characters[charIdx].pages.Count;
            }
        }

        // get widest bitmap
        Rectangle getLargestBitmapFromCharInfo(CharacterGenerationInfo[] charInfoArray)
        {
            // largest rect
            Rectangle largestRect = new Rectangle(0, 0, 0, 0);

            // iterate through chars
            for (int charIdx = 0; charIdx < charInfoArray.Length; ++charIdx)
            {
                // get the string of the characer
                string letterString = charInfoArray[charIdx].character.ToString();
                
                // measure the size of teh character in pixels
                Size stringSize = TextRenderer.MeasureText(letterString, charInfoArray[charIdx].fontInfo.font);

                // check if larger
                largestRect.Height = Math.Max(largestRect.Height, stringSize.Height);
                largestRect.Width = Math.Max(largestRect.Width, stringSize.Width);
            }

            // return largest
            return largestRect;
        }

        // populate the font info
        private FontInfo populateFontInfo(Font font)
        {
            // the font information
            FontInfo fontInfo = new FontInfo();

            // get teh characters we need to generate from the input text, removing duplicates
            fontInfo.generatedChars = getCharactersToGenerate();

            // set font into into
            fontInfo.font = font;

            // array holding all bitmaps and info per character
            fontInfo.characters = new CharacterGenerationInfo[fontInfo.generatedChars.Length];

            //
            // init char infos
            //
            for (int charIdx = 0; charIdx < fontInfo.generatedChars.Length; ++charIdx)
            {
                // create char info entity
                fontInfo.characters[charIdx] = new CharacterGenerationInfo();

                // point back to teh font
                fontInfo.characters[charIdx].fontInfo = fontInfo;

                // set the character
                fontInfo.characters[charIdx].character = fontInfo.generatedChars[charIdx];
            }
            
            //
            // Find the widest bitmap size we are going to draw
            //
            Rectangle largestBitmap = getLargestBitmapFromCharInfo(fontInfo.characters);
            
            //
            // create bitmaps per characater
            //

            // iterate over characters
            for (int charIdx = 0; charIdx < fontInfo.generatedChars.Length; ++charIdx)
            {
                // generate the original bitmap for the character
                convertCharacterToBitmap(fontInfo.generatedChars[charIdx], 
                                         font, 
                                         out fontInfo.characters[charIdx].bitmapOriginal, largestBitmap);

                // save
                // fontInfo.characters[charIdx].bitmapOriginal.Save(String.Format("C:/bms/{0}.bmp", fontInfo.characters[charIdx].character));
            }

            //
            // iterate through all bitmaps and find the tightest common border. only perform
            // this if the configuration specifies
            //

            // this will contain the values of the tightest border around the characters
            BitmapBorder tightestCommonBorder = new BitmapBorder();

            // only perform if padding type specifies
            if (m_outputConfig.paddingRemovalHorizontal == OutputConfiguration.PaddingRemoval.Fixed ||
                m_outputConfig.paddingRemovalVertical == OutputConfiguration.PaddingRemoval.Fixed)
            {
                // find the common tightest border
                findTightestCommonBitmapBorder(fontInfo.characters, ref tightestCommonBorder);
            }

            //
            // iterate thruogh all bitmaps and generate the bitmap we will convert to string
            // this means performing all manipulation (pad remove, flip)
            //

            // iterate over characters
            for (int charIdx = 0; charIdx < fontInfo.generatedChars.Length; ++charIdx)
            {
                // generate the original bitmap for the character
                manipulateBitmap(fontInfo.characters[charIdx].bitmapOriginal,
                                 tightestCommonBorder,
                                 out fontInfo.characters[charIdx].bitmapToGenerate,
                                 m_outputConfig.spaceGenerationPixels,
                                 fontInfo.characters[charIdx].bitmapOriginal.Height);

                // for debugging
                // fontInfo.characters[charIdx].bitmapToGenerate.Save(String.Format("C:/bms/{0}_cropped.bmp", fontInfo.characters[charIdx].character));
            }

            //
            // iterate through all characters and create the page array
            //

            // iterate over characters
            for (int charIdx = 0; charIdx < fontInfo.generatedChars.Length; ++charIdx)
            {
                // check if bitmap exists
                if (fontInfo.characters[charIdx].bitmapToGenerate != null)
                {
                    // create the page array for the character
                    convertBitmapToPageArray(fontInfo.characters[charIdx].bitmapToGenerate, out fontInfo.characters[charIdx].pages);
                }
            }

            // populate font info
            populateFontInfoFromCharacters(ref fontInfo);

            // return the font info
            return fontInfo;
        }

        // convert a page to string according to the output format
        private string convertPageToString(byte page, ref string charVisualizer)
        {
            // add leading character
            string resultString = m_outputConfig.byteLeadingString;
            
            // check format
            if (m_outputConfig.byteFormat == OutputConfiguration.ByteFormat.Hex)
            {
                // convert byte to hex
                resultString += page.ToString("X").PadLeft(2, '0');
            }
            else
            {
                // convert byte to binary
                resultString += Convert.ToString(page, 2).PadLeft(8, '0');
            }

            // iterate through bits, left to right, and visualize
            for (int bitMask = 0x80; bitMask != 0; bitMask >>= 1)
            {
                // check if bit is set
                if ((bitMask & page) != 0) charVisualizer += m_outputConfig.bmpVisualizerChar;
                else charVisualizer += " ";
            }
            
            // add comma
            resultString += ", ";
            
            // return the result
            return resultString;
        }

        // generate string from character info
        private string generateStringFromPageArray(ArrayList pages, int pagesPerRow)
        {
            // result string
            string resultString = "\t";

            // the trailing visualizer string
            string trailingCharVisualizer = "";

            // iterate through pages
            for (int charIdx = 1; charIdx <= pages.Count; ++charIdx)
            {
                // convert the page to string
                resultString += convertPageToString((byte)pages[charIdx - 1], ref trailingCharVisualizer);

                // check if newline is required
                if (m_outputConfig.lineWrap == OutputConfiguration.LineWrap.AtColumn &&
                    (charIdx % pagesPerRow) == 0)
                {
                    // generate trailing char visualizer if required
                    if (m_outputConfig.commentCharVisualizer)
                    {
                        // add to result string
                        resultString += String.Format("{0}{1}{2}", 
                                                        m_commentStartString,
                                                        trailingCharVisualizer,
                                                        m_commentEndString);

                        // zero out trailing string
                        trailingCharVisualizer = "";
                    }
                    
                    // add newline
                    resultString += "\r\n";

                    // if not last, add tab
                    if (charIdx != pages.Count) resultString += "\t";
                }
            }

            // add newline if per bitmap
            if (m_outputConfig.lineWrap == OutputConfiguration.LineWrap.AtBitmap) resultString += "\r\n";

            // return the result
            return resultString;
        }

        // get the font name and format it
        private string getFontName(ref Font font)
        {
            // get font name
            string fontName = font.Name.Replace(" ", "") + Math.Round(font.Size) + "pt";

            // get first char
            char firstChar = fontName[0];

            // remove first char
            fontName = fontName.Substring(1, fontName.Length - 1);

            // return name
            return Char.ToLower(firstChar) + fontName;
        }

        // convert bits to bytes according to desc format
        private int convertValueByDescriptorFormat(OutputConfiguration.DescriptorFormat descFormat, int valueInBits)
        {
            // according to format
            if (descFormat == OutputConfiguration.DescriptorFormat.DisplayInBytes)
            {
                // get value in bytes
                int valueInBytes = valueInBits / 8;
                if (valueInBits % 8 != 0) valueInBytes++;

                // set into string
                return valueInBytes;
            }
            else
            {
                // no conversion required
                return valueInBits;
            }
        }

        // get teh character descriptor string
        private string getCharacterDescString(OutputConfiguration.DescriptorFormat descFormat, int valueInBits)
        {
            // don't display
            if (descFormat == OutputConfiguration.DescriptorFormat.DontDisplay) return "";

            // add comma and return
            return String.Format("{0}, ", convertValueByDescriptorFormat(descFormat, valueInBits));
        }

        // get teh character descriptor string
        string getCharacterDescName(string name, OutputConfiguration.DescriptorFormat descFormat)
        {
            // don't display
            if (descFormat == OutputConfiguration.DescriptorFormat.DontDisplay) return "";

            // create result string
            string descFormatName = "";

            // set value
            if (descFormat == OutputConfiguration.DescriptorFormat.DisplayInBits) descFormatName = "bits";
            if (descFormat == OutputConfiguration.DescriptorFormat.DisplayInBytes) descFormatName = "bytes";

            // add comma and return
            return String.Format("[Char {0} in {1}], ", name, descFormatName);
        }

        // get only the variable name from an expression in a specific format
        // e.g. input: const far unsigned int my_font[] = ; 
        //      output: my_font[]
        private string getVariableNameFromExpression(string expression)
        {
            // iterator
            int charIndex = 0;

            // invalid format string
            const string invalidFormatString = "##Invalid format##";

            //
            // Strip array parenthesis
            //

            // search for '[number, zero or more] '
            const string arrayRegexString = @"\[[0-9]*\]";

            // modify the expression
            expression = Regex.Replace(expression, arrayRegexString, "");

            //
            // Find the string between '=' and a space, trimming spaces from end
            //

            // start at the end and look for the letter or number
            for (charIndex = expression.Length - 1; charIndex != 1; --charIndex)
            {
                // find the last character of the variable name
                if (expression[charIndex] != '=' && expression[charIndex] != ' ') break;
            }

            // check that its valid
            if (charIndex == 0) return invalidFormatString;

            // save this index
            int lastVariableNameCharIndex = charIndex;

            // continue looking for a space
            for (charIndex = lastVariableNameCharIndex; charIndex != 0; --charIndex)
            {
                // find the last character of the variable name
                if (expression[charIndex] == ' ') break;
            }

            // check that its valid
            if (charIndex == 0) return invalidFormatString;

            // save this index as well
            int firstVariableNameCharIndex = charIndex + 1;

            // return the substring
            return expression.Substring(firstVariableNameCharIndex, lastVariableNameCharIndex - firstVariableNameCharIndex + 1);
        }

        // generate the strings
        private void generateStringsFromFontInfo(FontInfo fontInfo, ref string resultTextSource, ref string resultTextHeader)
        {
            //
            // Character bitmaps
            //

            // according to config
            if (m_outputConfig.commentVariableName)
            {
                // add source header
                resultTextSource += String.Format("{0}Character bitmaps for {1} {2}pt{3}\r\n",
                                                    m_commentStartString, fontInfo.font.Name,
                                                    Math.Round(fontInfo.font.Size), m_commentEndString);
            }

            // get bitmap name
            string charBitmapVarName = String.Format(m_outputConfig.varNfBitmaps, getFontName(ref fontInfo.font));

            // header var
            resultTextHeader += String.Format("extern {0};\r\n", charBitmapVarName);

            // source var
            resultTextSource += String.Format("{0} = \r\n{{\r\n", charBitmapVarName);

            // iterate through letters
            for (int charIdx = 0; charIdx < fontInfo.characters.Length; ++charIdx)
            {
                // skip empty bitmaps
                if (fontInfo.characters[charIdx].bitmapToGenerate == null) continue;

                // according to config
                if (m_outputConfig.commentCharDescriptor)
                {
                    // output character header
                    resultTextSource += String.Format("\t{0}@{1} '{2}' ({3} pixels wide){4}\r\n",
                                                        m_commentStartString,
                                                        fontInfo.characters[charIdx].offsetInBytes,
                                                        fontInfo.characters[charIdx].character,
                                                        fontInfo.characters[charIdx].width,
                                                        m_commentEndString);
                }

                // get pages per row
                int pagesPerRow = fontInfo.characters[charIdx].bitmapToGenerate.Width / 8;
                if (fontInfo.characters[charIdx].bitmapToGenerate.Width % 8 != 0) pagesPerRow++;

                // now add letter array
                resultTextSource += generateStringFromPageArray(fontInfo.characters[charIdx].pages, pagesPerRow);

                // space out
                if (charIdx != fontInfo.characters.Length - 1 && m_outputConfig.commentCharDescriptor)
                {
                    // space between chars
                    resultTextSource += "\r\n";
                }
            }

            // space out
            resultTextSource += "};\r\n\r\n";

            //
            // Charater descriptor
            //

            // check if required by configuration
            if (m_outputConfig.generateLookupArray)
            {
                // according to config
                if (m_outputConfig.commentVariableName)
                {
                    // result string
                    resultTextSource += String.Format("{0}Character descriptors for {1} {2}pt{3}\r\n",
                                                        m_commentStartString, fontInfo.font.Name,
                                                        Math.Round(fontInfo.font.Size), m_commentEndString);

                    // describe character array
                    resultTextSource += String.Format("{0}{{ {1}{2}[Offset into {3}CharBitmaps in bytes] }}{4}\r\n",
                                                        m_commentStartString,
                                                        getCharacterDescName("width", m_outputConfig.descCharWidth),
                                                        getCharacterDescName("height", m_outputConfig.descCharHeight),
                                                        getFontName(ref fontInfo.font),
                                                        m_commentEndString);
                }

                // character name
                string charDescriptorVarName = String.Format(m_outputConfig.varNfCharInfo, getFontName(ref fontInfo.font));

                // add character array for header
                resultTextHeader += String.Format("extern {0};\r\n", charDescriptorVarName);

                // array for characters
                resultTextSource += String.Format("{0} =\r\n{{\r\n", charDescriptorVarName);

                // iterate over characters
                for (char character = fontInfo.startChar; character <= fontInfo.endChar; ++character)
                {
                    int width, height, offset;

                    // get char index
                    int charIndex = fontInfo.generatedChars.IndexOf(character);

                    // check if we generated this character
                    if (charIndex != -1)
                    {
                        // from char info
                        width = fontInfo.characters[charIndex].width;
                        height = fontInfo.characters[charIndex].height;
                        offset = fontInfo.characters[charIndex].offsetInBytes;
                    }
                    else
                    {
                        // unused
                        width = height = offset = 0;
                    }

                    // the end comment
                    string endComment = m_commentEndString;

                    // don't write '\' as the last character. shove a space after teh comment
                    if (m_outputConfig.commentStyle == OutputConfiguration.CommentStyle.Cpp)
                    {
                        // add a space
                        endComment += " ";
                    }

                    // add info
                    resultTextSource += String.Format("\t{{{0}{1}{2}}}, \t\t{3}{4}{5}\r\n",
                                                    getCharacterDescString(m_outputConfig.descCharWidth, width),
                                                    getCharacterDescString(m_outputConfig.descCharHeight, height),
                                                    offset,
                                                    m_commentStartString,
                                                    character,
                                                    endComment);
                }

                // terminate array
                resultTextSource += "};\r\n\r\n";
            }

            //
            // Font descriptor
            //
            
            // according to config
            if (m_outputConfig.commentVariableName)
            {
                // result string
                resultTextSource += String.Format("{0}Font information for {1} {2}pt{3}\r\n",
                                                    m_commentStartString,
                                                    fontInfo.font.Name, Math.Round(fontInfo.font.Size),
                                                    m_commentEndString);
            }

            // character name
            string fontInfoVarName = String.Format(m_outputConfig.varNfFontInfo, getFontName(ref fontInfo.font));

            // add character array for header
            resultTextHeader += String.Format("extern {0};\r\n", fontInfoVarName);

            // the font character height
            string fontCharHeightString = "", spaceCharacterPixelWidthString = "";
            
            // get character height sstring - displayed according to output configuration
            if (m_outputConfig.descFontHeight != OutputConfiguration.DescriptorFormat.DontDisplay)
            {
                // convert the value
                fontCharHeightString = String.Format("\t{0}, {1} Character height{2}\r\n",
                                              convertValueByDescriptorFormat(m_outputConfig.descFontHeight, fontInfo.charHeight),
                                              m_commentStartString,
                                              m_commentEndString);
            }

            // get space char width, if it is up to driver to generate
            if (!m_outputConfig.generateSpaceCharacterBitmap)
            {
                // convert the value
                spaceCharacterPixelWidthString = String.Format("\t{0}, {1} Width, in pixels, of space character{2}\r\n",
                                                                m_outputConfig.spaceGenerationPixels,
                                                                m_commentStartString,
                                                                m_commentEndString);
            }

            // font info
            resultTextSource += String.Format("{2} =\r\n{{\r\n" +
                                              "{3}" +
                                              "\t'{4}', {0} Start character{1}\r\n" +
                                              "\t'{5}', {0} End character{1}\r\n" +
                                              "{6}" +
                                              "\t{7}, {0} Character decriptor array{1}\r\n" +
                                              "\t{8}, {0} Character bitmap array{1}\r\n" +
                                              "}};\r\n",
                                              m_commentStartString,
                                              m_commentEndString,
                                              fontInfoVarName,
                                              fontCharHeightString,
                                              (char)fontInfo.startChar,
                                              (char)fontInfo.endChar,
                                              spaceCharacterPixelWidthString,
                                              getVariableNameFromExpression(String.Format(m_outputConfig.varNfCharInfo, getFontName(ref fontInfo.font))),
                                              getVariableNameFromExpression(String.Format(m_outputConfig.varNfBitmaps, getFontName(ref fontInfo.font))));
        }

        // generate the required output for text
        private void generateOutputForFont(Font font, ref string resultTextSource, ref string resultTextHeader)
        {
            // do nothing if no chars defined
            if (txtInputText.Text.Length == 0) return;
            
            // according to config
            if (m_outputConfig.commentVariableName)
            {
                // add source file header
                resultTextSource += String.Format("{0}\r\n{1} Font data for {2} {3}pt\r\n{4}\r\n\r\n",
                                                    m_commentStartString, m_commentBlockMiddleString, font.Name, Math.Round(font.Size),
                                                    m_commentBlockEndString);

                // add header file header
                resultTextHeader += String.Format("{0}Font data for {1} {2}pt{3}\r\n",
                                                    m_commentStartString, font.Name, Math.Round(font.Size),
                                                    m_commentEndString);
            }

            // populate the font info
            FontInfo fontInfo = populateFontInfo(font);
            
            // We now have all information required per font and per character. 
            // time to generate the string
            generateStringsFromFontInfo(fontInfo, ref resultTextSource, ref resultTextHeader);
        }

        // generate the required output for image
        private void generateOutputForImage(ref Bitmap bitmapOriginal, ref string resultTextSource, ref string resultTextHeader)
        {
            // the name of the bitmap
            string imageName = txtImageName.Text;

            // check if bitmap is assigned
            if (m_currentLoadedBitmap != null)
            {
                //
                // Bitmap manipulation
                //

                // get bitmap border
                BitmapBorder bitmapBorder = new BitmapBorder();
                getBitmapBorder(bitmapOriginal, bitmapBorder);

                // manipulate the bitmap
                Bitmap bitmapManipulated;

                // try to manipulate teh bitmap
                if (!manipulateBitmap(bitmapOriginal, bitmapBorder, out bitmapManipulated, 0, 0))
                {
                    // show error
                    MessageBox.Show("No black pixels found in bitmap (currently only monochrome bitmaps supported)",
                                    "Can't convert bitmap",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);

                    // stop here, failed to manipulate the bitmap for whatever reason
                    return;
                }

                // for debugging
                // bitmapManipulated.Save(String.Format("C:/bms/manip.bmp"));

                // according to config
                if (m_outputConfig.commentVariableName)
                {
                    // add source file header
                    resultTextSource += String.Format("{0}\r\n{1} Image data for {2}\r\n{3}\r\n\r\n",
                                                        m_commentStartString, m_commentBlockMiddleString, imageName,
                                                        m_commentBlockEndString);

                    // add header file header
                    resultTextHeader += String.Format("{0}Bitmap info for {1}{2}\r\n",
                                                        m_commentStartString, imageName,
                                                        m_commentEndString);
                }

                // bitmap varname
                string dataVarName = String.Format(m_outputConfig.varNfBitmaps, imageName);

                // add to header
                resultTextHeader += String.Format("extern {0};\r\n", dataVarName);

                // add header
                resultTextSource += String.Format("{0} =\r\n{{\r\n", dataVarName);

                //
                // Bitmap to string
                //

                // page array
                ArrayList pages;

                // first convert to pages
                convertBitmapToPageArray(bitmapManipulated, out pages);

                // assign pages for fully populated 8 bits
                int pagesPerRow = convertValueByDescriptorFormat(OutputConfiguration.DescriptorFormat.DisplayInBytes, bitmapManipulated.Width);

                // now convert to string
                resultTextSource += generateStringFromPageArray(pages, pagesPerRow);

                // close
                resultTextSource += String.Format("}};\r\n\r\n");

                // according to config
                if (m_outputConfig.commentVariableName)
                {
                    // set sizes comment
                    resultTextSource += String.Format("{0}Bitmap sizes for {1}{2}\r\n",
                                                        m_commentStartString, imageName, m_commentEndString);
                }

                // get var name
                string heightVarName = String.Format(m_outputConfig.varNfHeight, imageName);
                string widthVarName = String.Format(m_outputConfig.varNfWidth, imageName);

                // add sizes to header
                resultTextHeader += String.Format("extern {0};\r\n", widthVarName);
                resultTextHeader += String.Format("extern {0};\r\n", heightVarName);

                // add sizes to source
                resultTextSource += String.Format("{0} = {1};\r\n", widthVarName, pagesPerRow);
                resultTextSource += String.Format("{0} = {1};\r\n", heightVarName, bitmapManipulated.Height);
            }
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            // set focus somewhere else
            label1.Focus();
            
            // save default input text
            Properties.Settings.Default.InputText = txtInputText.Text;
            Properties.Settings.Default.Save();

            // will hold the resutl string            
            string resultStringSource = "";
            string resultStringHeader = "";

            // check which tab is active
            if (tcInput.SelectedTab.Text == "Text")
            {
                // generate output text
                generateOutputForFont(fontDlgInputFont.Font, ref resultStringSource, ref resultStringHeader);
            }
            else
            {
                // generate output bitmap
                generateOutputForImage(ref m_currentLoadedBitmap, ref resultStringSource, ref resultStringHeader);
            }

            // color code the strings and output
            outputSyntaxColoredString(resultStringSource, ref txtOutputTextSource);
            outputSyntaxColoredString(resultStringHeader, ref txtOutputTextHeader);
        }

        private void btnBitmapLoad_Click(object sender, EventArgs e)
        {
            // set filter
            dlgOpenFile.Filter = "Image Files (*.jpg; *.jpeg; *.gif; *.bmp)|*.jpg; *.jpeg; *.gif; *.bmp";

            // open the dialog
            if (dlgOpenFile.ShowDialog() != DialogResult.Cancel)
            {
                // load the bitmap
                m_currentLoadedBitmap = new Bitmap(dlgOpenFile.FileName);

                // try to open the bitmap
                pbxBitmap.Image = m_currentLoadedBitmap;

                // set the path
                txtImagePath.Text = dlgOpenFile.FileName;

                // guess a name
                txtImageName.Text = Path.GetFileNameWithoutExtension(dlgOpenFile.FileName);
            }
        }

        // parse the output text line
        void outputSyntaxColoredString(string outputString, ref RichTextBox outputTextBox)
        {
            // clear the current text
            outputTextBox.Text = "";
            
            // split output string
            Regex r = new Regex("\\n");
            String [] lines = r.Split(outputString);

            // for now don't syntax color for more than 2000 lines
            if (lines.Length > 1500)
            {
                // just set text
                outputTextBox.Text = outputString;
                return;
            }

            // iterate over the richtext box and color it
            foreach (string line in lines)
            {
                r = new Regex("([ \\t{}();])");
                String[] tokens = r.Split(line);

                // for each found token
                foreach (string token in tokens)
                {
                    // Set the token's default color and font.
                    outputTextBox.SelectionColor = Color.Black;
                    outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Regular);

                    // Check for a comment.
                    if (token == "//" || token.StartsWith("//"))
                    {
                        // Find the start of the comment and then extract the whole comment.
                        int index = line.IndexOf("//");
                        string comment = line.Substring(index, line.Length - index);
                        outputTextBox.SelectionColor = Color.Green;
                        outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Regular);
                        outputTextBox.SelectedText = comment;
                        break;
                    }

                    // Check for a comment. TODO: terminate coloring
                    if (token == "/*" || token.StartsWith("/*"))
                    {
                        // Find the start of the comment and then extract the whole comment.
                        int index = line.IndexOf("/*");
                        string comment = line.Substring(index, line.Length - index);
                        outputTextBox.SelectionColor = Color.Green;
                        outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Regular);
                        outputTextBox.SelectedText = comment;
                        break;
                    }

                    // Check for a comment. TODO: terminate coloring
                    if (token == "**" || token.StartsWith("**"))
                    {
                        // Find the start of the comment and then extract the whole comment.
                        int index = line.IndexOf("**");
                        string comment = line.Substring(index, line.Length - index);
                        outputTextBox.SelectionColor = Color.Green;
                        outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Regular);
                        outputTextBox.SelectedText = comment;
                        break;
                    }

                    // Check for a comment. TODO: terminate coloring
                    if (token == "*/" || token.StartsWith("*/"))
                    {
                        // Find the start of the comment and then extract the whole comment.
                        int index = line.IndexOf("*/");
                        string comment = line.Substring(index, line.Length - index);
                        outputTextBox.SelectionColor = Color.Green;
                        outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Regular);
                        outputTextBox.SelectedText = comment;
                        break;
                    }

                    // Check whether the token is a keyword. 
                    String[] keywords = { "uint_8", "const", "extern", "char", "unsigned", "int", "short", "long" };
                    for (int i = 0; i < keywords.Length; i++)
                    {
                        if (keywords[i] == token)
                        {
                            // Apply alternative color and font to highlight keyword.
                            outputTextBox.SelectionColor = Color.Blue;
                            outputTextBox.SelectionFont = new Font("Courier New", 10, FontStyle.Bold);
                            break;
                        }
                    }

                    // set the token text
                    outputTextBox.SelectedText = token;
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // close self
            Close();
        }

        private void splitContainer1_MouseUp(object sender, MouseEventArgs e)
        {
            // no focus
            label1.Focus();
        }

        private void btnInsertText_Click(object sender, EventArgs e)
        {
            // no focus
            label1.Focus();

            // insert text
            txtInputText.Text += ((ComboBoxItem)cbxTextInsert.SelectedItem).value;
        }

        private void aboutToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            // about
            AboutForm about = new AboutForm();
            about.FormBorderStyle = FormBorderStyle.FixedToolWindow;

            // show teh about form
            about.Show();
        }
        
        // set comment strings according to config
        private void updateCommentStrings()
        {
            if (m_outputConfig.commentStyle == OutputConfiguration.CommentStyle.Cpp)
            {
                // strings for comments
                m_commentStartString = "// ";
                m_commentBlockEndString = m_commentBlockMiddleString = m_commentStartString;
                m_commentEndString = "";
            }
            else
            {
                // strings for comments
                m_commentStartString = "/* ";
                m_commentBlockMiddleString = "** ";
                m_commentEndString = " */";
                m_commentBlockEndString = "*/";
            }
        }
        
        private void btnOutputConfig_Click(object sender, EventArgs e)
        {
            // no focus
            label1.Focus();

            // get it
            OutputConfigurationForm outputConfigForm = new OutputConfigurationForm(ref m_outputConfigurationManager);
            
            // get the oc
            int selectedConfigurationIndex = outputConfigForm.getOutputConfiguration(cbxOutputConfiguration.SelectedIndex);

            // update the dropdown
            m_outputConfigurationManager.comboboxPopulate(cbxOutputConfiguration);

            // get working configuration
            m_outputConfig = m_outputConfigurationManager.workingOutputConfiguration;

            // set selected index
            cbxOutputConfiguration.SelectedIndex = selectedConfigurationIndex;

            // update comment strings according to conifg
            updateCommentStrings();
        }

        private void button4_Click(object sender, EventArgs e)
        {
        }

        private void cbxOutputConfiguration_SelectedIndexChanged(object sender, EventArgs e)
        {
            // check if any configuration selected
            if (cbxOutputConfiguration.SelectedIndex != -1)
            {
                // get the configuration
                m_outputConfig = m_outputConfigurationManager.configurationGetAtIndex(cbxOutputConfiguration.SelectedIndex);
            }

            // save selected index for next time
            Properties.Settings.Default.OutputConfigIndex = cbxOutputConfiguration.SelectedIndex;

            // save
            Properties.Settings.Default.Save();
        }

        private void button4_Click_1(object sender, EventArgs e)
        {
            
        }

        private void tsmCopySource_Click(object sender, EventArgs e)
        {
            // copy if any text
            if (txtOutputTextSource.Text != "")
            {
                // copy
                Clipboard.SetText(txtOutputTextSource.Text);
            }
        }

        private void tsmCopyHeader_Click(object sender, EventArgs e)
        {
            // copy if any text
            if (txtOutputTextHeader.Text != "")
            {
                // copy
                Clipboard.SetText(txtOutputTextHeader.Text);
            }
        }

        private void ctxMenuHeader_Opening(object sender, CancelEventArgs e)
        {

        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // zero out file name
            dlgSaveAs.FileName = "";

            // try to prompt
            if (dlgSaveAs.ShowDialog() != DialogResult.Cancel)
            {
                // get the file name
                string moduleName = dlgSaveAs.FileName;

                // save the text
                txtOutputTextSource.SaveFile(String.Format("{0}.c", moduleName), RichTextBoxStreamType.PlainText);
                txtOutputTextHeader.SaveFile(String.Format("{0}.h", moduleName), RichTextBoxStreamType.PlainText);
            }
        }
    }
}
