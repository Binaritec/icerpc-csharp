// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    interface EncodedResultOperations
    {
        [cs:encoded-result] AnotherStruct opAnotherStruct1(AnotherStruct p1);
        [cs:encoded-result] (AnotherStruct r1, AnotherStruct r2) opAnotherStruct2(AnotherStruct p1);

        [cs:encoded-result] StringSeq opStringSeq1(StringSeq p1);
        [cs:encoded-result] (StringSeq r1, StringSeq r2) opStringSeq2(StringSeq p1);

        [cs:encoded-result] StringDict opStringDict1(StringDict p1);
        [cs:encoded-result] (StringDict r1, StringDict r2) opStringDict2(StringDict p1);

        [cs:encoded-result] MyClassA opMyClassA(MyClassA p1);
    }
}