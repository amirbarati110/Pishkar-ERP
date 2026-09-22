namespace ERP.Persistence.Migrations;

/// <summary>
/// «لیست مشتریان» (checklist «ن-۲»): the customer record grows from name + mobile to what a shop
/// and an electronic tax invoice need — kind (حقیقی / حقوقی / خارجی), first and last name, company,
/// identity number, economic code, registration number, postal code, phone, email, birth date,
/// notes, and a customer number (شماره اشتراک) from a sequence.
///
/// <para>Only the structure is changed here. Filling the new columns for customers that already
/// exist happens in V017 and the constraints that need them filled come in V018: Firebird does not
/// let a statement use a column added earlier in the same transaction, and each migration is one
/// transaction.</para>
/// </summary>
public sealed class V016CustomerProfile : IMigration
{
    public int Version => 16;

    public string Name => "Add customer kind, names, identity numbers, contact details and customer number";

    public IReadOnlyList<string> Statements { get; } =
    [
        "CREATE SEQUENCE SEQ_CUSTOMER_CODE",
        "ALTER TABLE CUSTOMER ADD CODE BIGINT",
        "ALTER TABLE CUSTOMER ADD KIND SMALLINT DEFAULT 1 NOT NULL",
        "ALTER TABLE CUSTOMER ADD FIRST_NAME VARCHAR(120) CHARACTER SET UTF8",
        "ALTER TABLE CUSTOMER ADD LAST_NAME VARCHAR(120) CHARACTER SET UTF8",
        "ALTER TABLE CUSTOMER ADD COMPANY_NAME VARCHAR(120) CHARACTER SET UTF8",
        "ALTER TABLE CUSTOMER ADD NATIONAL_ID VARCHAR(15) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD ECONOMIC_CODE VARCHAR(14) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD REGISTRATION_NUMBER VARCHAR(15) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD POSTAL_CODE CHAR(10) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD PHONE VARCHAR(11) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD EMAIL VARCHAR(100) CHARACTER SET UTF8",
        "ALTER TABLE CUSTOMER ADD BIRTH_DATE CHAR(10) CHARACTER SET ASCII",
        "ALTER TABLE CUSTOMER ADD NOTES VARCHAR(500) CHARACTER SET UTF8",
        // NULL is allowed many times in a Firebird unique constraint, so customers without an
        // identity number do not collide with each other.
        "ALTER TABLE CUSTOMER ADD CONSTRAINT UQ_CUSTOMER_NATIONAL_ID UNIQUE (NATIONAL_ID)",
    ];
}
