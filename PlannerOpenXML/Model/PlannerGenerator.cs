﻿using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using PlannerOpenXML.Services;

namespace PlannerOpenXML.Model
{
    public class PlannerGenerator
    {
        private readonly ApiService _apiService;

        public PlannerGenerator()
        {
            _apiService = new ApiService();
        }
        public async Task GeneratePlanner(int? year, int? firstMonth, int? numberOfMonths)
        {
            if (year == null || firstMonth == null || numberOfMonths == null)
            {
                MessageBox.Show("Please fill in all the fields.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "Select file path to save the planner";
            saveFileDialog.Filter = "Excel files (*.xlsx)|*.xlsx";
            saveFileDialog.FileName = "Planner";

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(saveFileDialog.FileName, SpreadsheetDocumentType.Workbook))
                    {
                        WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
                        workbookPart.Workbook = new Workbook();

                        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                        worksheetPart.Worksheet = new Worksheet(new SheetData());

                        Stylesheet workbookstylesheet = GeneratorStylesheet();

                        WorkbookStylesPart stylesheet = workbookPart.AddNewPart<WorkbookStylesPart>();
                        stylesheet.Stylesheet = workbookstylesheet;

                        Sheets sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
                        Sheet sheet = new Sheet() { Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Planner" };
                        sheets.Append(sheet);

                        CultureInfo culture = new CultureInfo("de-DE");

                        int currentYear = year ?? 0;
                        int currentMonth = firstMonth ?? 0;

                        var germanHolidaysTask = _apiService.GetHolidaysAsync(currentYear, "DE");
                        var hungarianHolidaysTask = _apiService.GetHolidaysAsync(currentYear, "HU");

                        var germanHolidays = await germanHolidaysTask;
                        var hungarianHolidays = await hungarianHolidaysTask;

                        for (int i = 0; i < numberOfMonths; i++)
                        {
                            currentYear = year.Value;
                            currentMonth = firstMonth.Value + i;

                            while (currentMonth > 12)
                            {
                                currentYear++;
                                currentMonth -= 12;

                                germanHolidaysTask = _apiService.GetHolidaysAsync(currentYear, "DE");
                                hungarianHolidaysTask = _apiService.GetHolidaysAsync(currentYear, "HU");
                                
                                germanHolidays = await germanHolidaysTask;
                                hungarianHolidays = await hungarianHolidaysTask;
                            }

                            DateTime monthDate = new DateTime(currentYear, currentMonth, 1);
                            string monthName = monthDate.ToString("MMMM yyyy", culture);
                            
                            Cell monthYearCell = new Cell(new CellValue($"{monthName}"))
                            {
                                DataType = CellValues.String,
                                StyleIndex = 1
                            };
                             
                            AppendCellToWorksheet(spreadsheetDocument, worksheetPart, monthYearCell, 1, (uint)(i + 1));

                            int currentRow = 2;

                            DateTime currentDate = monthDate;
                            while (currentDate.Month == monthDate.Month)
                            {
                                string dayOfWeek = currentDate.ToString("ddd", culture);
                                string cellValue = $"{currentDate.Day} {dayOfWeek}";

                                string germanHolidayName = GetHolidayName(currentDate, (List<Holiday>)germanHolidays);
                                string hungarianHolidayName = GetHolidayName(currentDate, (List<Holiday>)hungarianHolidays);

                                DateTime nextMonth = monthDate.AddMonths(1);
                                bool isLastDayOfMonth = currentDate.AddDays(1).Month != nextMonth.Month;

                                Cell dateCell = new Cell(new CellValue(cellValue))
                                {
                                    StyleIndex = 2
                                };

                                if (!string.IsNullOrEmpty(germanHolidayName) && !string.IsNullOrEmpty(hungarianHolidayName))
                                    cellValue += $" DE&HU: {germanHolidayName}";
                                else if (!string.IsNullOrEmpty(germanHolidayName))
                                    cellValue += $" DE: {germanHolidayName}";
                                else if (!string.IsNullOrEmpty(hungarianHolidayName))
                                    cellValue += $" HU: {hungarianHolidayName}";

                                dateCell = new Cell(new CellValue(cellValue))
                                {
                                    DataType = CellValues.String,
                                    StyleIndex = 2
                                };
                                
                                if (!isLastDayOfMonth)
                                {
                                    if (currentDate.DayOfWeek == DayOfWeek.Saturday)
                                        dateCell.StyleIndex = 9;
                                    else if (currentDate.DayOfWeek == DayOfWeek.Sunday)
                                        dateCell.StyleIndex = 10;
                                    else if (cellValue.Contains(" DE:"))
                                        dateCell.StyleIndex = 11;
                                    else if (cellValue.Contains(" HU:"))
                                        dateCell.StyleIndex = 12;
                                    else if (cellValue.Contains(" DE&HU:"))
                                        dateCell.StyleIndex = 13;
                                    else
                                        dateCell.StyleIndex = 8;
                                }
                                else if (currentDate.DayOfWeek == DayOfWeek.Saturday)
                                    dateCell.StyleIndex = 3;
                                else if (currentDate.DayOfWeek == DayOfWeek.Sunday)
                                    dateCell.StyleIndex = 4;
                                else if (cellValue.Contains(" DE:"))
                                    dateCell.StyleIndex = 5;
                                else if (cellValue.Contains(" HU:"))
                                    dateCell.StyleIndex = 6;
                                else if (cellValue.Contains(" DE&HU:"))
                                    dateCell.StyleIndex = 7;

                                AppendCellToWorksheet(spreadsheetDocument, worksheetPart, dateCell, (uint)currentRow, (uint)(i + 1));
                                
                                currentDate = currentDate.AddDays(1);
                                currentRow++;
                            }

                            SetColumnWidth(worksheetPart, (uint)(i + 1), 35);
                        }

                        workbookPart.Workbook.Save();
                    }

                    MessageBox.Show($"Planner has been generated and saved as: {saveFileDialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while generating the planner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #region GeneratorStylesheet
        private Stylesheet GeneratorStylesheet()
        {
            Stylesheet workbookstylesheet = new Stylesheet();

                        Font font0 = new Font();

                        Font font1 = new Font();
                        Bold bold = new Bold();
                        font1.Append(bold);

                        Font font2 = new Font();
                        font2.Bold = new Bold();
                        font2.Color = new Color() { Rgb = new HexBinaryValue() { Value = "555555" } };

                        Font font3 = new Font();
                        font3.Bold = new Bold();
                        font3.Color = new Color() { Rgb = new HexBinaryValue() { Value = "FF0000" } };

                        Font font4 = new Font();
                        font4.Bold = new Bold();
                        font4.Color = new Color() { Rgb = new HexBinaryValue() { Value = "00008B" } };

                        Font font5 = new Font();
                        font5.Bold = new Bold();
                        font5.Color = new Color() { Rgb = new HexBinaryValue() { Value = "006400" } };

                        Font font6 = new Font();
                        font6.Bold = new Bold();
                        font6.Color = new Color() { Rgb = new HexBinaryValue() { Value = "C71585" } };

                        Fonts fonts = new Fonts();
                        fonts.Append(font0);
                        fonts.Append(font1);
                        fonts.Append(font2);
                        fonts.Append(font3);
                        fonts.Append(font4);
                        fonts.Append(font5);
                        fonts.Append(font6);

                        Fill fill0 = new Fill();

                        Fill fill1 = new Fill();
                        PatternFill patternFill1 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor1 = new ForegroundColor(){ Rgb = "FFFFFF" };
                        patternFill1.Append(foregroundColor1);
                        fill1.Append(patternFill1);

                        Fill fill2 = new Fill();
                        PatternFill patternFill2 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor2 = new ForegroundColor(){ Rgb = "DCDCDC" };
                        patternFill2.Append(foregroundColor2);
                        fill2.Append(patternFill2);

                        Fill fill3 = new Fill();
                        PatternFill patternFill3 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor3 = new ForegroundColor(){ Rgb = "F68072" };
                        patternFill3.Append(foregroundColor3);
                        fill3.Append(patternFill3);

                        Fill fill4 = new Fill();
                        PatternFill patternFill4 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor4 = new ForegroundColor(){ Rgb = "87CEFA" };
                        patternFill4.Append(foregroundColor4);
                        fill4.Append(patternFill4);

                        Fill fill5 = new Fill();
                        PatternFill patternFill5 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor5 = new ForegroundColor(){ Rgb = "9bff66" };
                        patternFill5.Append(foregroundColor5);
                        fill5.Append(patternFill5);

                        Fill fill6 = new Fill();
                        PatternFill patternFill6 = new PatternFill(){ PatternType = PatternValues.Solid };
                        ForegroundColor foregroundColor6 = new ForegroundColor(){ Rgb = "FFC0CB" };
                        patternFill6.Append(foregroundColor6);
                        fill6.Append(patternFill6);

                        Fills fills = new Fills();
                        fills.Append(fill0);
                        fills.Append(fill1);
                        fills.Append(fill2);
                        fills.Append(fill3);
                        fills.Append(fill4);
                        fills.Append(fill5);
                        fills.Append(fill6);

                        Border border0 = new Border(new LeftBorder(),
                                                    new RightBorder(),
                                                    new TopBorder(),
                                                    new BottomBorder());

                        Border border1 = new Border(new LeftBorder() { Style = BorderStyleValues.Thick },
                                                    new RightBorder() { Style = BorderStyleValues.Thick },
                                                    new TopBorder() { Style = BorderStyleValues.Thick },
                                                    new BottomBorder() { Style = BorderStyleValues.Thick });

                        Border border2 = new Border(new LeftBorder() { Style = BorderStyleValues.Thick },
                                                    new RightBorder() { Style = BorderStyleValues.Thick },
                                                    new TopBorder() { Style = BorderStyleValues.Thin },
                                                    new BottomBorder() { Style = BorderStyleValues.Thin });

                        Border border3 = new Border(new LeftBorder() { Style = BorderStyleValues.Thick },
                                                    new RightBorder() { Style = BorderStyleValues.Thick },
                                                    new TopBorder() { Style = BorderStyleValues.Thin },
                                                    new BottomBorder() { Style = BorderStyleValues.Thick });

                        Borders borders = new Borders();
                        borders.Append(border0);
                        borders.Append(border1);
                        borders.Append(border2);
                        borders.Append(border3);

                        CellFormat defaultStyle = new CellFormat()
                        {
                            FormatId = 0,
                            FillId = 0,
                            BorderId = 0
                        };

                        Alignment alignment = new Alignment()
                        {
                            Horizontal = HorizontalAlignmentValues.Center,
                            Vertical = VerticalAlignmentValues.Center
                        };

                        CellFormat nameOfMonthStyle = new CellFormat(alignment)
                        {
                            FontId = 1,
                            BorderId = 1
                        };

                        CellFormat borderStyle = new CellFormat()
                        {
                            BorderId = 2
                        };

                        CellFormat saturdayStyle = new CellFormat()
                        {
                            BorderId = 2,
                            FontId = 2,
                            FillId = 2,
                        };

                        CellFormat sundayStyle = new CellFormat()
                        {
                            BorderId = 2,
                            FontId = 3,
                            FillId = 3,
                        };

                        CellFormat germanHolidayStyle = new CellFormat()
                        {
                            BorderId = 2,
                            FontId = 4,
                            FillId = 4,
                        };

                        CellFormat hungarianHolidayStyle = new CellFormat()
                        {
                            BorderId = 2,
                            FontId = 5,
                            FillId = 5
                        };

                        CellFormat germanAndHungarianHolidayStyle = new CellFormat()
                        {
                            BorderId = 2,
                            FontId = 6,
                            FillId = 6
                        };

                        CellFormat lastDayOfMonthStyle = new CellFormat()
                        {
                            BorderId = 3
                        };

                        CellFormat lastDayOfMonthAndSaturdayStyle = new CellFormat()
                        {
                            BorderId = 3,
                            FontId = 2,
                            FillId = 2
                        };

                        CellFormat lastDayOfMonthAndSundayStyle = new CellFormat()
                        {
                            BorderId = 3,
                            FontId = 3,
                            FillId = 3
                        };

                        CellFormat lastDayOfMonthAndGermanHolidayStyle = new CellFormat()
                        {
                            BorderId = 3,
                            FontId = 4,
                            FillId = 4
                        };

                        CellFormat lastDayOfMonthAndHungarianHolidayStyle = new CellFormat()
                        {
                            BorderId = 3,
                            FontId = 5,
                            FillId = 5
                        };

                        CellFormat lastDayOfMonthAndGermanAndHungarianHolidayStyle = new CellFormat()
                        {
                            BorderId = 3,
                            FontId = 6,
                            FillId = 6
                        };

                        CellFormats cellformats = new CellFormats();
                        cellformats.Append(defaultStyle);
                        cellformats.Append(nameOfMonthStyle);
                        cellformats.Append(borderStyle);
                        cellformats.Append(saturdayStyle);
                        cellformats.Append(sundayStyle);
                        cellformats.Append(germanHolidayStyle);
                        cellformats.Append(hungarianHolidayStyle);
                        cellformats.Append(germanAndHungarianHolidayStyle);
                        cellformats.Append(lastDayOfMonthStyle);
                        cellformats.Append(lastDayOfMonthAndSaturdayStyle);
                        cellformats.Append(lastDayOfMonthAndSundayStyle);
                        cellformats.Append(lastDayOfMonthAndGermanHolidayStyle);
                        cellformats.Append(lastDayOfMonthAndHungarianHolidayStyle);
                        cellformats.Append(lastDayOfMonthAndGermanAndHungarianHolidayStyle);

                        workbookstylesheet.Append(fonts);
                        workbookstylesheet.Append(fills);
                        workbookstylesheet.Append(borders);
                        workbookstylesheet.Append(cellformats);

            return workbookstylesheet;
        }
        #endregion GeneratorStylesheet
        
        #region AppendCellToWorksheet
        private void AppendCellToWorksheet(SpreadsheetDocument spreadsheetDocument, WorksheetPart worksheetPart, Cell cell, uint rowIndex, uint columnIndex)
        {
            SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            Row row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex == rowIndex);
            if (row == null)
            {
                row = new Row() { RowIndex = rowIndex };
                sheetData.Append(row);
            }
            else
            {
                Cell existingCell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference == $"{GetColumnName(columnIndex)}{rowIndex}");

                if (existingCell != null)
                {
                    row.RemoveChild(existingCell);
                }
            }
            cell.CellReference = $"{GetColumnName(columnIndex)}{rowIndex}";
            row.Append(cell);
        }
        #endregion AppendCellToWorksheet

        #region GetColumnName
        private string GetColumnName(uint columnIndex)
        {
            uint dividend = columnIndex;
            string columnName = String.Empty;
            uint modifier;

            while (dividend > 0)
            {
                modifier = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modifier).ToString() + columnName;
                dividend = (uint)((dividend - modifier) / 26);
            }

            return columnName;
        }
        #endregion GetColumnName

        #region SetColumnWidth
        private static void SetColumnWidth(WorksheetPart worksheetPart, uint columnIndex, double width)
        {
            DocumentFormat.OpenXml.Spreadsheet.Columns columns = worksheetPart.Worksheet.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Columns>();
            if (columns == null)
            {
                columns = new DocumentFormat.OpenXml.Spreadsheet.Columns();
                worksheetPart.Worksheet.InsertAt(columns, 0);
            }

            DocumentFormat.OpenXml.Spreadsheet.Column column = new DocumentFormat.OpenXml.Spreadsheet.Column()
            {
                Min = columnIndex,
                Max = columnIndex,
                Width = width,
                CustomWidth = true
            };

            columns.Append(column);
        }
        #endregion SetColumnWidth

        #region GetHolidayName
        private string GetHolidayName(DateTime date, List<Holiday> holidays)
        {
            foreach (var holiday in holidays)
            {
                DateTime holidayDate = DateTime.Parse(holiday.Date);
                if (holidayDate.Date == date.Date)
                {
                    return holiday.Name;
                }
            }
            return null;
        }
        #endregion GetHolidayName
    }
}