OPack Pseudo-BNF
===================

Basic Types
-------------------

The following basic types are used as terminals in the rest of the grammar.

 * uint5 5-bits unsigned integer
 * uint7 7-bits unsigned integer
 * uint8 8-bits unsigned integer
 * uint16 16-bits little-endian unsigned integer
 * uint32 32-bits little-endian unsigned integer
 * float 32-bits (IEEE 754 single precision floating point)
 * double 64-bits (IEEE 754 double precision floating point)
 * The expression "\xNN" indicates a single unsigned byte with the hexadecimal value 0xNN
 * The expression "\bN" indicates a 1-bit unsigned integer
 * The expression "\bNNN" indicates a 3-bits unsigned integer


Rules
-------------------

```
document ::=
    uint32 index_table_list uint32 element_list ; the first uint32 is the number of index-tables
                                                ; the second uin32 is the number of elements in list

index_table ::=
    "\x01" uint8 element_list      ; Tiny index-table, uint8 is the number of elements.
  | "\x02" uint16 element_list     ; Small index-table, uint16 is the number of elements.
  | "\x03" uint32 element_list     ; Normal index-table, uint32 is the number of elements.

index_table_list ::=
    index_table index_table_list
  | ""

element_list ::=
    element element_list
  | ""

element ::=
    atomic_values                  ; Atomic values.
  | map                            ; Map is a dictionary representing an object.
  | array                          ; Array is a sequence of elements.
  | string                         ; A simple sequence of characters in UTF-8.
  | number                         ; An integer or float point number.
  | indexed_element                ; Indexed element is a back reference to a value.

atomic_values ::=
    "\x01"                         ; A null value.
  | "\x02"                         ; A True boolean value.
  | "\x03"                         ; A False boolean value.

number ::=
    "\x0B" int8                    ; 1-byte signed integer value.
  | "\x0C" int16                   ; 2-bytes little-endian signed integer value.
  | "\x0D" int32                   ; 4-bytes little-endian signed integer value.
  | "\x0E" int64                   ; 8-bytes little-endian signed integer value.
  | "\x0F" uint8                   ; 1-byte unsigned integer value.
  | "\x10" uint16                  ; 2-bytes little-endian unsigned integer value.
  | "\x11" uint32                  ; 4-bytes little-endian unsigned integer value.
  | "\x12" uint64                  ; 8-bytes little-endian unsigned integer value.
  | "\x13" float                   ; 4-bytes IEEE 754 single precision floating point.
  | "\x14" double                  ; 8-bytes IEEE 754 double precision floating point.

array ::=
    "\b101" uint5 element_list     ; uint5 is the number of elements in the array.
  | "\x08" uint8 element_list      ; uint8 is the number of elements.
  | "\x09" uint16 element_list     ; uint16 is the number of elements.
  | "\x0A" uint32 element_list     ; uint32 is the number of elements.

map ::=
    "\b110" uint5 key_element_list   ; uint5 is the number of elements.
  | "\x05" uint8 key_element_list    ; uint8 is the number of elements.
  | "\x06" uint16 key_element_list   ; uint16 is the number of elements.
  | "\x07" uint32 key_element_list   ; uint32 is the number of elements.

key_element_list ::=
    "\b0" uint7 element              ; uint7 is the position of the key in the first index table.
  | "\b1" uint7 uint8  element       ; uint7 is the position of a Tiny IndexTable where the key
                                     ; can be found using uint8 as index.
  | "\b1" uint7 uint16 element       ; uint7 is the position of a Small IndexTable where the key
                                     ; can be found using uint16 as index.
  | "\b1" uint7 uint32 element       ; uint7 is the position of a Normal IndexTable where the key
                                     ; can be found using uint32 as index.

indexed_element ::=
    "\b110" uint5                    ; uint5 is the index of the value in the first index table.
  | uint8 uint8                      ; First uint8 is the table index and the second one is the
                                     ; index of the element in a index-table (Tiny).
  | uint8 uint16                     ; uint8 is the table index and uint16 is the index of the
                                     ; element in a index-table (Small).
  | uint8 uint32                     ; uint8 is the table index and uint32 is the index of the
                                     ; element in a index-table (Normal).

```
