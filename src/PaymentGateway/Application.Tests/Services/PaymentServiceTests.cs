﻿namespace Application.Tests.Services
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;
    using Application.Models;
    using Application.Services;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Repository.Interfaces;
    using Repository.Models;
    using Support.BankClient;
    using Xunit;

    [ExcludeFromCodeCoverage]
    public class PaymentServiceTests
    {
        private readonly Mock<IPaymentRepository> paymentRepositoryMock;
        private readonly Mock<IMerchantRepository> merchantRepositoryMock;
        private readonly Mock<IAcquiringBankClient> acquiringBankMock;
        private readonly Mock<ILogger<PaymentService>> loggerMock;
        private readonly PaymentService target;

        public PaymentServiceTests()
        {
            this.paymentRepositoryMock = new Mock<IPaymentRepository>();
            this.merchantRepositoryMock = new Mock<IMerchantRepository>();
            this.acquiringBankMock = new Mock<IAcquiringBankClient>();
            this.loggerMock = new Mock<ILogger<PaymentService>>();

            this.target = new PaymentService(
                this.paymentRepositoryMock.Object,
                this.merchantRepositoryMock.Object,
                this.acquiringBankMock.Object,
                this.loggerMock.Object);
        }

        [Fact]
        public async Task ProcessPaymentAsync_WhenMerchantNotExist_ReturnsError()
        {
            // Arrange
            var paymentRequest = new PaymentPostRequest
            {
                MerchantId = Guid.NewGuid(),
                Currency = "USD"
            };

            this.merchantRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(default(Merchant));

            // Act
            var result = await this.target.ProcessPaymentAsync(paymentRequest);

            // Assert
            Assert.False(result.Success);
            Assert.True(result.Errors.ContainsKey("merchantNotFound"));

            this.merchantRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task ProcessPaymentAsync_WhenCurrencyIsNotValid_ReturnsError()
        {
            // Arrange
            var paymentRequest = new PaymentPostRequest
            {
                MerchantId = Guid.NewGuid(),
                Currency = "XXX"
            };

            this.merchantRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new Merchant());

            // Act
            var result = await this.target.ProcessPaymentAsync(paymentRequest);

            // Assert
            Assert.False(result.Success);
            Assert.True(result.Errors.ContainsKey("currencyNotValid"));

            this.merchantRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task ProcessPaymentAsync_WhenAcquiringBankIsNull_ReturnsError()
        {
            // Arrange
            var paymentRequest = new PaymentPostRequest
            {
                MerchantId = Guid.NewGuid(),
                Currency = "USD"
            };

            this.merchantRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new Merchant());

            this.acquiringBankMock
                .Setup(s => s.ProcessTransactionAsync(It.IsAny<BankClientRequest>()))
                .ReturnsAsync(default(BankClientResult));

            // Act
            var result = await this.target.ProcessPaymentAsync(paymentRequest);

            // Assert
            Assert.False(result.Success);
            Assert.True(result.Errors.ContainsKey("bankClient"));

            this.merchantRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
            this.acquiringBankMock
                .Verify(v => v.ProcessTransactionAsync(It.IsAny<BankClientRequest>()), Times.Once);
        }

        [Fact]
        public async Task ProcessPaymentAsync_WhenPaymentRepositoryThrowsException_ReturnsError()
        {
            // Arrange
            var paymentRequest = new PaymentPostRequest
            {
                MerchantId = Guid.NewGuid(),
                Currency = "USD"
            };

            this.merchantRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new Merchant());

            this.acquiringBankMock
                .Setup(s => s.ProcessTransactionAsync(It.IsAny<BankClientRequest>()))
                .ReturnsAsync(new BankClientResult { TransactionId = Guid.NewGuid(), TransactionStatus = "ok" });

            this.paymentRepositoryMock
                .Setup(s => s.InsertAsync(It.IsAny<Payment>()))
                .ThrowsAsync(new Exception());

            // Act
            var result = await this.target.ProcessPaymentAsync(paymentRequest);

            // Assert
            Assert.False(result.Success);
            Assert.True(result.Errors.ContainsKey("paymentProcess"));

            this.merchantRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
            this.acquiringBankMock
                .Verify(v => v.ProcessTransactionAsync(It.IsAny<BankClientRequest>()), Times.Once);
            this.paymentRepositoryMock
                .Verify(v => v.InsertAsync(It.IsAny<Payment>()), Times.Once);
            this.loggerMock
                .Verify(v => v.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Equals("Error while processing payment.")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Once);
        }

        [Fact]
        public async Task ProcessPaymentAsync_HappyJourney_ReturnsPayment()
        {
            // Arrange
            var paymentRequest = new PaymentPostRequest
            {
                Amount = 1500,
                CardNumber = "1234567890123456",
                Currency = "USD",
                ExpiryMonth = 12,
                ExpiryYear = 2020,
                MerchantId = Guid.NewGuid(),
                SecurityCode = 123
            };

            this.merchantRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new Merchant());

            this.acquiringBankMock
                .Setup(s => s.ProcessTransactionAsync(It.IsAny<BankClientRequest>()))
                .ReturnsAsync(new BankClientResult { TransactionId = Guid.NewGuid(), TransactionStatus = "ok" });

            this.paymentRepositoryMock
                .Setup(s => s.InsertAsync(It.IsAny<Payment>()))
                .ReturnsAsync(new Payment { Id = Guid.NewGuid() });

            // Act
            var result = await this.target.ProcessPaymentAsync(paymentRequest);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(0, result.Errors.Count);
            Assert.NotNull(result.Result);

            this.merchantRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
            this.acquiringBankMock
                .Verify(v => v.ProcessTransactionAsync(It.IsAny<BankClientRequest>()), Times.Once);
            this.paymentRepositoryMock
                .Verify(v => v.InsertAsync(It.IsAny<Payment>()), Times.Once);
        }

        [Fact]
        public async Task RetrievePaymentDetailsAsync_WhenHasNoDetails_ReturnsError()
        {
            // Arrange
            var paymentRequest = new PaymentGetRequest
            {
                PaymentId = Guid.NewGuid()
            };

            this.paymentRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(default(Payment));

            // Act
            var result = await this.target.RetrievePaymentDetailsAsync(paymentRequest);

            // Assert
            Assert.False(result.Success);
            Assert.True(result.Errors.ContainsKey("paymentDetails"));

            this.paymentRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
            this.loggerMock
                .Verify(v => v.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Equals("Payment details not found.")),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)), Times.Once);
        }

        [Fact]
        public async Task RetrievePaymentDetailsAsync_HappyJourney_ReturnsDetails()
        {
            // Arrange
            var paymentRequest = new PaymentGetRequest
            {
                PaymentId = Guid.NewGuid()
            };

            this.paymentRepositoryMock
                .Setup(s => s.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new Payment { Id = Guid.NewGuid() });

            // Act
            var result = await this.target.RetrievePaymentDetailsAsync(paymentRequest);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(0, result.Errors.Count);
            Assert.NotNull(result.Result);

            this.paymentRepositoryMock
                .Verify(v => v.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
        }
    }
}