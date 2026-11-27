---
title: Quarterly figures
subtitle: First quarter 2026
author: Planning team
date: 2026-03-31
lang: en
classification: internal
---

# Quarterly figures

Sales, orders and stock of the first quarter, with every kind of column.

## Sales

Net revenue per region.

| Region {width=18} | Units {.integer total=sum} | Revenue {.currency=EUR decimals=2 total=sum} | Share {.percent decimals=1 total=sum} | Factor {.number decimals=3 total=average} | Since {.date} | Counted {.datetime} | Active {.boolean total=count} | Code {.text} |
| :---------------- | -------------------------: | -------------------------------------------: | ------------------------------------: | ----------------------------------------: | ------------: | ------------------: | :---------------------------: | :----------- |
| North | 1,200 | 15,000.50 | 25 % | 1.234 | 2024-01-01 | 2026-03-01 14:30 | true | 01234 |
| South | 800 | 9,800 | 18 % | 0.987 | 6/1/2025 | 2026-03-02 09:05 | false | A-77 |
| West | 3,100 | 41,000.75 | 57 % | 1.5 | 2025-09-15 | 2026-03-03 16:00 | yes | B\|C |

Table: Sales by region

```chart
category: Region
series: Revenue
title: Revenue by region
source: Ledger
```

## Orders

| Item | Price | Quantity {total=sum} | Amount {total=sum} | Ordered | Paid |
| ---- | ----: | -------------------: | -----------------: | ------- | ---- |
| Pens | $1.50 | 10 | =[Price]*[Quantity] | 2026-01-15 | true |
| Pads | $3.20 | 4 | =[Price]*[Quantity] | 2026-02-03 | false |
| Ink | $12.00 | 2 | =[Price]*[Quantity] | 2026-03-09 | true |

```chart
type: horizontal-bar
category: Item
series: Quantity
placement: sheet
sheet: Quantities
```

## Months

Orders by month, from the controlling report.

```chart
type: line
title: Orders by month
unit: k
---
month, plan, actual
Jan, 120, 132.5
Feb, 118, 121
Mar, 125, 140
```
